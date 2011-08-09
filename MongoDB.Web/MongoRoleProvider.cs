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
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration.Provider;
using System.Linq;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Security.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    public class MongoRoleProvider : RoleProvider {

        public override string ApplicationName { get; set; }

        private string _connectionString;
        private string _databaseName;

        public override void Initialize(string name, NameValueCollection config) {            
            if (name == null) {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0) {
                throw new ArgumentException(ProviderResources.Common_ProviderNameHasZeroLength, "name");
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

            // Initialize collections.
            ProviderHelper.InitializeCollections(ApplicationName, _connectionString, _databaseName);
        }

        public override bool IsUserInRole(string userName, string roleName) {
            throw new NotImplementedException();
        }

        public override string[] GetRolesForUser(string userName) {
            throw new NotImplementedException();
        }

        public override void CreateRole(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                throw new ArgumentException(ProviderResources.Role_RoleNameCannotBeNullOrWhiteSpace, "roleName");
            }
            if (roleName.Contains(",")) {
                throw new ArgumentException(string.Format("Role name cannot contain the '{0}' character.", ','));
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
                    throw new ProviderException("Role name already exists.");
                }
                
                throw new ProviderException("Could not create role.", e);                
            }
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
            throw new NotImplementedException();
        }

        public override bool RoleExists(string roleName) {
            if (string.IsNullOrWhiteSpace(roleName)) {
                throw new ArgumentException(ProviderResources.Role_RoleNameCannotBeNullOrWhiteSpace, "roleName");
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
                throw new ArgumentException(ProviderResources.Membership_UserNameCannotBeNullOrWhiteSpace);
            }
            if (roleNames.Any(string.IsNullOrWhiteSpace)) {
                throw new ArgumentException(ProviderResources.Role_RoleNameCannotBeNullOrWhiteSpace);
            }

            var users = GetUserCollection();
            var roles = GetRoleCollection();
            var userNamesBsonArray = BsonArray.Create(userNames.AsEnumerable());
            var roleNamesBsonArray = BsonArray.Create(roleNames.AsEnumerable());

            try {
                var query = Query.In("UserName", userNamesBsonArray);
                var userCount = users.Count(query);
                if (userCount != userNames.Length) {
                    throw new ProviderException(ProviderResources.Membership_UserDoesNotExist);
                }    
            } catch (MongoSafeModeException e) {
                
            }

            try {
                var query = Query.In("RoleName", roleNamesBsonArray);
                var roleCount = roles.Count(query);
                if (roleCount != roleNames.Length) {
                    throw new ProviderException(ProviderResources.Role_RoleDoesNotExist);
                }
            } catch (MongoSafeModeException e) {

            }

            try {                
                var query = Query.And(
                    Query.In("UserName", userNamesBsonArray),
                    Query.In("Roles.RoleName", roleNamesBsonArray));
                var userCount = users.Count(query);
                if (userCount != userNames.Length) {
                    throw new ProviderException(ProviderResources.Role_UserIsAlreadyInRole);
                }
            } catch (MongoSafeModeException e) {

            }

            try {
                var query = Query.In("UserName", userNamesBsonArray);
                var update = Update.PushAll("Roles", roleNames.Select(BsonValue.Create));
                users.Update(query, update);
            } catch (MongoSafeModeException e) {

            }
        }

        public override void RemoveUsersFromRoles(string[] userNames, string[] roleNames) {
            throw new NotImplementedException();
        }

        public override string[] GetUsersInRole(string roleName) {
            throw new NotImplementedException();
        }

        public override string[] GetAllRoles() {
            throw new NotImplementedException();
        }

        public override string[] FindUsersInRole(string roleName, string userNameToMatch) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private MongoCollection<MongoMembershipUser> GetUserCollection() {
            return ProviderHelper.GetCollectionAs<MongoMembershipUser>(ApplicationName, _connectionString, _databaseName, "users");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(ObjectId id) {
            return ProviderHelper.GetMongoUser(GetUserCollection(), id);
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
        /// <returns></returns>
        private MongoCollection<MongoRole> GetRoleCollection() {
            return ProviderHelper.GetCollectionAs<MongoRole>(ApplicationName, _connectionString, _databaseName, "roles");
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
