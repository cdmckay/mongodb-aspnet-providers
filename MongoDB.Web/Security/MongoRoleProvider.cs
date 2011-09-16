#region License
// Copyright 2011 Cameron McKay
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Linq;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    public class MongoRoleProvider : RoleProvider {

        public override string ApplicationName { get; set; }

        private bool _enableTrace;
        private TraceSource _traceSource;

        private string _connectionString;
        private string _databaseName;
        private SafeMode _safeMode;

        public override void Initialize(string name, NameValueCollection config) {            
            if (name == null) {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0) {
                throw new ArgumentException(ProviderResources.ProviderNameHasZeroLength, "name");
            }
            if (string.IsNullOrWhiteSpace(config["description"])) {
                config.Remove("description");
                config["description"] = "MongoDB Role Provider";
            }

            // Initialize the base class.
            base.Initialize(name, config);

            _enableTrace = Convert.ToBoolean(config["enableTrace"] ?? "false");
            if (_enableTrace) {
                _traceSource = new TraceSource(GetType().Name, SourceLevels.All);
            }

            // Deal with the application name.           
            ApplicationName = ProviderHelper.ResolveApplicationName(config);

            // Get the connection string.
            _connectionString = ProviderHelper.ResolveConnectionString(config);

            // Get the database name.
            var mongoUrl = new MongoUrl(_connectionString);
            _databaseName = mongoUrl.DatabaseName;

            _safeMode = ProviderHelper.GenerateSafeMode(config);

            // Initialize collections.
            ProviderHelper.InitializeCollections(ApplicationName, _connectionString, _databaseName, _safeMode);
        }

        public override bool IsUserInRole(string userName, string roleName) {
            if (!UserExists(userName)) {
                var message = ProviderResources.UserDoesNotExist;
                throw TraceException("IsUserInRole", new ArgumentException(message, "userName"));
            }
            if (!RoleExists(roleName)) {
                var message = ProviderResources.RoleDoesNotExist;
                throw TraceException("IsUserInRole", new ArgumentException(message, "roleName"));
            }

            var query = Query.And(
                Query.EQ("UserName", userName),
                Query.EQ("Roles", roleName));
            try {
                var users = GetUserCollection();
                var user = users.FindOne(query);
                return user != null;
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRetrieveUsersInRoles;
                throw TraceException("IsUserInRole", new ProviderException(message, e));
            }
        }

        public override string[] GetRolesForUser(string userName) {
            // TODO Implement.
            throw new NotImplementedException();
        }

        public override void CreateRole(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                var message = ProviderResources.RoleNameCannotBeNullOrWhiteSpace;
                throw TraceException("CreateRole", new ArgumentException(message, "roleName"));
            }
            if (roleName.Contains(",")) {
                var message = string.Format(ProviderResources.RoleNameCannotContainCharacter, ',');
                throw TraceException("CreateRole", new ArgumentException(message));
            }

            var newRole = new MongoRole {
                Id = ObjectId.GenerateNewId(),
                RoleName = roleName
            };

            try {
                var roles = GetRoleCollection();
                roles.Insert(newRole);
            } catch (MongoSafeModeException e) {
                string message;
                if (e.Message.Contains("RoleName_1")) {
                    message = ProviderResources.RoleNameAlreadyExists;                    
                } else {
                    message = ProviderResources.CouldNotCreateRole;
                }
                throw TraceException("CreateRole", new ProviderException(message, e));                
            }
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
            if (!RoleExists(roleName)) {
                var message = ProviderResources.RoleDoesNotExist;
                throw TraceException("DeleteRole", new ArgumentException(message, "roleName"));
            }

            if (throwOnPopulatedRole && GetUsersInRole(roleName).Length > 0) {
                var message = ProviderResources.CannotDeletePopulatedRoles;
                throw TraceException("DeleteRole", new ProviderException(message));
            }
            
            try {
                var roleQuery = Query.EQ("RoleName", roleName);
                var roles = GetRoleCollection();
                roles.Remove(roleQuery);

                var userQuery = Query.EQ("Roles", roleName);
                var userUpdate = Update.Pull("Roles", roleName);
                var users = GetUserCollection();
                users.Update(userQuery, userUpdate, UpdateFlags.Multi);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRemoveRole;
                throw TraceException("DeleteRole", new ProviderException(message, e)); 
            }

            return true;
        }

        public override bool RoleExists(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                var message = ProviderResources.RoleNameCannotBeNullOrWhiteSpace;
                throw TraceException("RoleExists", new ArgumentException(message, "roleName"));
            }

            return GetMongoRole(roleName) != null;
        }

        public override void AddUsersToRoles(string[] userNames, string[] roleNames) {
            if (userNames == null) {
                throw TraceException("AddUsersToRoles", new ArgumentNullException("userNames"));
            }
            if (roleNames == null) {
                throw TraceException("AddUsersToRoles", new ArgumentNullException("roleNames"));
            }
            if (userNames.Any(string.IsNullOrWhiteSpace)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("AddUsersToRoles", new ArgumentException(message));
            }
            if (roleNames.Any(string.IsNullOrWhiteSpace)) {
                var message = ProviderResources.RoleNameCannotBeNullOrWhiteSpace;
                throw TraceException("AddUsersToRoles", new ArgumentException(message));
            }

            var users = GetUserCollection();
            var roles = GetRoleCollection();
            var userNamesBsonArray = BsonArray.Create(userNames.AsEnumerable());
            var roleNamesBsonArray = BsonArray.Create(roleNames.AsEnumerable());

            try {
                // Check if any users do not exist.
                var userCount = users.Count(Query.In("UserName", userNamesBsonArray));
                if (userCount != userNames.Length) {
                    var message = ProviderResources.UserDoesNotExist;
                    throw TraceException("AddUsersToRoles", new ProviderException(message));
                }    
          
                // Check if any roles do not exist.
                var roleCount = roles.Count(Query.In("RoleName", roleNamesBsonArray));
                if (roleCount != roleNames.Length) {
                    var message = ProviderResources.RoleDoesNotExist;
                    throw TraceException("AddUsersToRoles", new ProviderException(message));
                }
                    
                // Make sure none of the users already have some of the roles.
                var userInRole = users.FindOne(Query.And(
                    Query.In("UserName", userNamesBsonArray),
                    Query.In("Roles", roleNamesBsonArray)));
                if (userInRole != null) {
                    var message = ProviderResources.UserIsAlreadyInRole;
                    throw TraceException("AddUsersToRoles", new ProviderException(message));
                }
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRetrieveUsersInRoles;
                throw TraceException("AddUsersToRoles", new ProviderException(message, e));
            }

            try {                
                var query = Query.In("UserName", userNamesBsonArray);
                var update = Update.PushAll("Roles", roleNames.Select(BsonValue.Create));
                users.Update(query, update, UpdateFlags.Multi);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotAddUsersToRoles;
                throw TraceException("AddUsersToRoles", new ProviderException(message, e));
            }
        }

        public override void RemoveUsersFromRoles(string[] userNames, string[] roleNames) {
            if (userNames == null) {
                throw TraceException("RemoveUsersFromRoles", new ArgumentNullException("userNames"));
            }
            if (roleNames == null) {
                throw TraceException("RemoveUsersFromRoles", new ArgumentNullException("roleNames"));
            }
            if (userNames.Any(string.IsNullOrWhiteSpace)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("RemoveUsersFromRoles", new ArgumentException(message));
            }
            if (roleNames.Any(string.IsNullOrWhiteSpace)) {
                var message = ProviderResources.RoleNameCannotBeNullOrWhiteSpace;
                throw TraceException("RemoveUsersFromRoles", new ArgumentException(message));
            }

            var users = GetUserCollection();
            var roles = GetRoleCollection();
            var userNamesBsonArray = BsonArray.Create(userNames.AsEnumerable());
            var roleNamesBsonArray = BsonArray.Create(roleNames.AsEnumerable());

            try {
                // Check if any users do not exist.
                var userCount = users.Count(Query.In("UserName", userNamesBsonArray));
                if (userCount != userNames.Length) {
                    var message = ProviderResources.UserDoesNotExist;
                    throw TraceException("RemoveUsersFromRoles", new ProviderException(message));
                }

                // Check if any roles do not exist.
                var roleCount = roles.Count(Query.In("RoleName", roleNamesBsonArray));
                if (roleCount != roleNames.Length) {
                    var message = ProviderResources.RoleDoesNotExist;
                    throw TraceException("RemoveUsersFromRoles", new ProviderException(message));
                }

                // Make sure each user is in at least one role.
                var userInRoleCount = users.Count(Query.And(
                    Query.In("UserName", userNamesBsonArray),
                    Query.All("Roles", roleNamesBsonArray)));
                if (userInRoleCount != userNames.Length) {
                    var message = ProviderResources.UserIsNotInRole;
                    throw TraceException("RemoveUsersFromRoles", new ProviderException(message));
                }
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotCountUsersInRoles;
                throw TraceException("RemoveUsersFromRoles", new ProviderException(message, e));
            }

            try {
                var query = Query.In("UserName", userNamesBsonArray);
                var update = Update.PullAll("Roles", roleNames.Select(BsonValue.Create));
                users.Update(query, update, UpdateFlags.Multi);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRemoveUsersFromRoles;
                throw TraceException("RemoveUsersFromRoles", new ProviderException(message, e));
            }
        }

        public override string[] GetUsersInRole(string roleName) {
            if (!RoleExists(roleName)) {
                var message = ProviderResources.RoleDoesNotExist;
                throw TraceException("GetUsersInRole", new ArgumentException(message, "roleName"));
            }

            try {
                var query = Query.EQ("Roles", roleName);
                var users = GetUserCollection();
                return users.Find(query)
                    .Select(u => u.UserName)
                    .ToArray();
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRetrieveUsersInRoles;
                throw TraceException("GetUsersInRole", new ProviderException(message, e));
            }
        }

        public override string[] GetAllRoles() {
            try {
                var roles = GetRoleCollection();
                return roles.FindAll()
                    .Select(r => r.RoleName)
                    .ToArray();
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRetrieveRoles;
                throw TraceException("GetAllRoles", new ProviderException(message, e));
            }
        }

        public override string[] FindUsersInRole(string roleName, string userNameToMatch) {
            if (!RoleExists(roleName)) {
                var message = ProviderResources.RoleDoesNotExist;
                throw TraceException("FindUsersInRole", new ArgumentException(message, "roleName"));
            }
            if (userNameToMatch == null) {
                throw TraceException("FindUsersInRole", new ArgumentNullException("userNameToMatch"));
            }

            try {
                var query = Query.And(
                    Query.Matches("UserName", userNameToMatch),
                    Query.EQ("Roles", roleName));
                var users = GetUserCollection();
                return users.Find(query)
                    .Select(u => u.UserName)
                    .ToArray();
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRetrieveUsersInRoles;
                throw TraceException("FindUsersInRole", new ProviderException(message, e));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private MongoCollection<MongoMembershipUser> GetUserCollection() {
            return ProviderHelper.GetCollectionAs<MongoMembershipUser>(ApplicationName, _connectionString, _databaseName, _safeMode, "users");
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(string userName) {
            return ProviderHelper.GetMongoUser(_traceSource, GetUserCollection(), userName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        private bool UserExists(string userName) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("UserExists", new ArgumentException(message, "userName"));
            }

            return GetMongoUser(userName) != null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private MongoCollection<MongoRole> GetRoleCollection() {
            return ProviderHelper.GetCollectionAs<MongoRole>(ApplicationName, _connectionString, _databaseName, _safeMode, "roles");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="roleName"></param>
        /// <returns></returns>
        private MongoRole GetMongoRole(string roleName) {
            return ProviderHelper.GetMongoRole(_traceSource, GetRoleCollection(), roleName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="e"></param>
        private Exception TraceException(string methodName, Exception e) {
            return ProviderHelper.TraceException(_traceSource, methodName, e);
        }
        
    }
}
