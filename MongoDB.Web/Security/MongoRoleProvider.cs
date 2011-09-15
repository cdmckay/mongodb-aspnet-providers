﻿#region License
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
using System.Linq;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    public class MongoRoleProvider : RoleProvider {

        public override string ApplicationName { get; set; }

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
                throw new ArgumentException(ProviderResources.UserDoesNotExist, "userName");
            }
            if (!RoleExists(roleName)) {
                throw new ArgumentException(ProviderResources.RoleDoesNotExist, "roleName");
            }

            var query = Query.And(
                Query.EQ("UserName", userName),
                Query.EQ("Roles", roleName));
            try {
                var users = GetUserCollection();
                var user = users.FindOne(query);
                return user != null;
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRetrieveUsersInRoles, e);
            }
        }

        public override string[] GetRolesForUser(string userName) {
            throw new NotImplementedException();
        }

        public override void CreateRole(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                throw new ArgumentException(ProviderResources.RoleNameCannotBeNullOrWhiteSpace, "roleName");
            }
            if (roleName.Contains(",")) {
                throw new ArgumentException(string.Format(ProviderResources.RoleNameCannotContainCharacter, ','));
            }

            var newRole = new MongoRole {
                Id = ObjectId.GenerateNewId(),
                RoleName = roleName
            };

            try {
                var roles = GetRoleCollection();
                roles.Insert(newRole);
            } catch (MongoSafeModeException e) {
                if (e.Message.Contains("RoleName_1")) {
                    throw new ProviderException(ProviderResources.RoleNameAlreadyExists);
                }
                
                throw new ProviderException(ProviderResources.CouldNotCreateRole, e);                
            }
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
            if (!RoleExists(roleName)) {
                throw new ArgumentException(ProviderResources.RoleDoesNotExist, "roleName");
            }

            if (throwOnPopulatedRole && GetUsersInRole(roleName).Length > 0) {
                throw new ProviderException(ProviderResources.CannotDeletePopulatedRoles);
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
                throw new ProviderException(ProviderResources.CouldNotRemoveRole, e); 
            }

            return true;
        }

        public override bool RoleExists(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                throw new ArgumentException(ProviderResources.RoleNameCannotBeNullOrWhiteSpace, "roleName");
            }

            return GetMongoRole(roleName) != null;
        }

        public override void AddUsersToRoles(string[] userNames, string[] roleNames) {
            if (userNames == null) {
                throw new ArgumentNullException("userNames");
            }
            if (roleNames == null) {
                throw new ArgumentNullException("roleNames");
            }
            if (userNames.Any(string.IsNullOrWhiteSpace)) {
                throw new ArgumentException(ProviderResources.UserNameCannotBeNullOrWhiteSpace);
            }
            if (roleNames.Any(string.IsNullOrWhiteSpace)) {
                throw new ArgumentException(ProviderResources.RoleNameCannotBeNullOrWhiteSpace);
            }

            var users = GetUserCollection();
            var roles = GetRoleCollection();
            var userNamesBsonArray = BsonArray.Create(userNames.AsEnumerable());
            var roleNamesBsonArray = BsonArray.Create(roleNames.AsEnumerable());

            try {
                // Check if any users do not exist.
                var userCount = users.Count(Query.In("UserName", userNamesBsonArray));
                if (userCount != userNames.Length) {
                    throw new ProviderException(ProviderResources.UserDoesNotExist);
                }    
          
                // Check if any roles do not exist.
                var roleCount = roles.Count(Query.In("RoleName", roleNamesBsonArray));
                if (roleCount != roleNames.Length) {
                    throw new ProviderException(ProviderResources.RoleDoesNotExist);
                }
                    
                // Make sure none of the users already have some of the roles.
                var userInRole = users.FindOne(Query.And(
                    Query.In("UserName", userNamesBsonArray),
                    Query.In("Roles", roleNamesBsonArray)));
                if (userInRole != null) {
                    throw new ProviderException(ProviderResources.UserIsAlreadyInRole);
                }
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRetrieveUsersInRoles, e);
            }

            try {                
                var query = Query.In("UserName", userNamesBsonArray);
                var update = Update.PushAll("Roles", roleNames.Select(BsonValue.Create));
                users.Update(query, update, UpdateFlags.Multi);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotAddUsersToRoles, e);
            }
        }

        public override void RemoveUsersFromRoles(string[] userNames, string[] roleNames) {
            if (userNames == null) {
                throw new ArgumentNullException("userNames");
            }
            if (roleNames == null) {
                throw new ArgumentNullException("roleNames");
            }
            if (userNames.Any(string.IsNullOrWhiteSpace)) {
                throw new ArgumentException(ProviderResources.UserNameCannotBeNullOrWhiteSpace);
            }
            if (roleNames.Any(string.IsNullOrWhiteSpace)) {
                throw new ArgumentException(ProviderResources.RoleNameCannotBeNullOrWhiteSpace);
            }

            var users = GetUserCollection();
            var roles = GetRoleCollection();
            var userNamesBsonArray = BsonArray.Create(userNames.AsEnumerable());
            var roleNamesBsonArray = BsonArray.Create(roleNames.AsEnumerable());

            try {
                // Check if any users do not exist.
                var userCount = users.Count(Query.In("UserName", userNamesBsonArray));
                if (userCount != userNames.Length) {
                    throw new ProviderException(ProviderResources.UserDoesNotExist);
                }

                // Check if any roles do not exist.
                var roleCount = roles.Count(Query.In("RoleName", roleNamesBsonArray));
                if (roleCount != roleNames.Length) {
                    throw new ProviderException(ProviderResources.RoleDoesNotExist);
                }

                // Make sure each user is in at least one role.
                var userInRoleCount = users.Count(Query.And(
                    Query.In("UserName", userNamesBsonArray),
                    Query.All("Roles", roleNamesBsonArray)));
                if (userInRoleCount != userNames.Length) {
                    throw new ProviderException(ProviderResources.UserIsNotInRole);
                }
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotCountUsersInRoles, e);
            }

            try {
                var query = Query.In("UserName", userNamesBsonArray);
                var update = Update.PullAll("Roles", roleNames.Select(BsonValue.Create));
                users.Update(query, update, UpdateFlags.Multi);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRemoveUsersFromRoles, e);
            }
        }

        public override string[] GetUsersInRole(string roleName) {
            if (!RoleExists(roleName)) {
                throw new ArgumentException(ProviderResources.RoleDoesNotExist, "roleName");
            }

            try {
                var query = Query.EQ("Roles", roleName);
                var users = GetUserCollection();
                return users.Find(query)
                    .Select(u => u.UserName)
                    .ToArray();
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRetrieveUsersInRoles, e);
            }
        }

        public override string[] GetAllRoles() {
            try {
                var roles = GetRoleCollection();
                return roles.FindAll()
                    .Select(r => r.RoleName)
                    .ToArray();
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRetrieveRoles, e);
            }
        }

        public override string[] FindUsersInRole(string roleName, string userNameToMatch) {
            if (!RoleExists(roleName)) {
                throw new ArgumentException(ProviderResources.RoleDoesNotExist, "roleName");
            }
            if (userNameToMatch == null) {
                throw new ArgumentNullException("userNameToMatch");
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
                throw new ProviderException(ProviderResources.CouldNotRetrieveUsersInRoles, e);
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
            return ProviderHelper.GetMongoUser(GetUserCollection(), userName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        public bool UserExists(string userName) {
            if (string.IsNullOrWhiteSpace(userName)) {
                throw new ArgumentException(ProviderResources.UserNameCannotBeNullOrWhiteSpace, "userName");
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
            return ProviderHelper.GetMongoRole(GetRoleCollection(), roleName);
        }
        
    }
}
