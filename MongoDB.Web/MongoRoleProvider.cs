using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Security.Resources;
using MongoDB.Driver;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    public class MongoRoleProvider : RoleProvider {

        public override string ApplicationName {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }

        public override void Initialize(string name, NameValueCollection config) {            
            if (name == null) {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0) {
                throw new ArgumentException(ProviderResources.Common_ProviderNameHasZeroLength, "name");
            }

            // Initialize the base class.
            base.Initialize(name, config);
        }

        public override bool IsUserInRole(string userName, string roleName) {
            throw new NotImplementedException();
        }

        public override string[] GetRolesForUser(string userName) {
            throw new NotImplementedException();
        }

        public override void CreateRole(string roleName) {
            throw new NotImplementedException();
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole) {
            throw new NotImplementedException();
        }

        public override bool RoleExists(string roleName) {
            throw new NotImplementedException();
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
            //var server = MongoServer.Create(_connectionString);
            //var database = server.GetDatabase(_databaseName, SafeMode.True);
            //return database.GetCollection<MongoMembershipUser>(ApplicationName + ".users");
            return null;
        }
        
    }
}
