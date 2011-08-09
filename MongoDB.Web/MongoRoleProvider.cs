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
            if (string.IsNullOrEmpty(roleName)) {
                throw new ArgumentException(ProviderResources.RoleProvider_RoleNameCannotBeNullOrWhiteSpace, "roleName");
            }
            if (roleName.Contains(",")) {
                throw new ProviderException(string.Format("Role name cannot contain the '{0}' character.", ','));
            }

            var newRole = new MongoRole {
                Id = ObjectId.GenerateNewId(),
                Name = roleName
            };

            try {
                var roles = GetRoleCollection();
                roles.Insert(newRole);
            } catch (MongoSafeModeException e) {
                if (e.Message.Contains("Name_1")) {
                    throw new ProviderException("Role name already exists.");
                }
                
                throw new ProviderException("Could not create role.", e);                
            }
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
            throw new NotImplementedException();
        }

        public override bool RoleExists(string roleName) {
            if (string.IsNullOrEmpty(roleName)) {
                throw new ArgumentException(ProviderResources.RoleProvider_RoleNameCannotBeNullOrWhiteSpace, "roleName");
            }

            return GetMongoRole(roleName) != null;
        }

        public override void AddUsersToRoles(string[] userNames, string[] roleNames) {
            throw new NotImplementedException();
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
            MongoRole role;
            try {
                var roles = GetRoleCollection();
                role = roles.FindOneAs<MongoRole>(Query.EQ("Name", roleName));    
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve role.", e);
            }
            return role;
        }
        
    }
}
