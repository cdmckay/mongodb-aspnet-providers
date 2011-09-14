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

using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Linq;
using System.Web.Hosting;
using DigitalLiberationFront.MongoDB.Web.Profile;
using DigitalLiberationFront.MongoDB.Web.Resources;
using DigitalLiberationFront.MongoDB.Web.Security;
using DigitalLiberationFront.MongoDB.Web.SessionState;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web {
    internal static class ProviderHelper {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static string ResolveApplicationName(NameValueCollection config) {
            var applicationName = config["applicationName"];
            if (string.IsNullOrEmpty(applicationName)) {
                applicationName = HostingEnvironment.ApplicationVirtualPath;
            } else if (applicationName.Contains('\0')) {
                throw new ProviderException(string.Format(ProviderResources.ApplicationNameCannotContainCharacter, @"\0"));
            } else if (applicationName.Contains('$')) {
                throw new ProviderException(string.Format(ProviderResources.ApplicationNameCannotContainCharacter, @"$"));
            }
            return applicationName;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static string ResolveConnectionString(NameValueCollection config) {
            var connectionStringSettings = ConfigurationManager.ConnectionStrings[config["connectionStringName"]];
            return connectionStringSettings != null ? connectionStringSettings.ConnectionString.Trim() : string.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="applicationName"></param>
        /// <param name="connectionString"></param>
        /// <param name="databaseName"></param>
        public static void InitializeCollections(string applicationName, string connectionString, string databaseName) {
            // Initialize users collection.
            var users = GetCollectionAs<MongoMembershipUser>(applicationName, connectionString, databaseName, "users");
            if (!users.Exists()) {
                users.ResetIndexCache();
                users.EnsureIndex(IndexKeys.Ascending("UserName"), IndexOptions.SetUnique(true));
                users.EnsureIndex(IndexKeys.Ascending("Email"));
                users.EnsureIndex(IndexKeys.Ascending("IsAnonymous"));
            }

            // Initialize roles collection.
            var roles = GetCollectionAs<MongoRole>(applicationName, connectionString, databaseName, "roles");
            if (!roles.Exists()) {
                roles.ResetIndexCache();
                roles.EnsureIndex(IndexKeys.Ascending("RoleName"), IndexOptions.SetUnique(true));
            }

            // Initialize sessions collection.
            var sessions = GetCollectionAs<MongoSession>(applicationName, connectionString, databaseName, "sessions");
            if (!sessions.Exists()) {
                sessions.ResetIndexCache();
                sessions.EnsureIndex(IndexKeys.Ascending("SessionId"));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="applicationName"></param>
        /// <param name="connectionString"></param>
        /// <param name="databaseName"></param>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public static MongoCollection<T> GetCollectionAs<T>(string applicationName, string connectionString, string databaseName,
                                                            string collectionName) {
            var server = MongoServer.Create(connectionString);
            var database = server.GetDatabase(databaseName, SafeMode.True);
            return database.GetCollection<T>(applicationName + "." + collectionName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="users"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static MongoMembershipUser GetMongoUser(MongoCollection<MongoMembershipUser> users, ObjectId id) {
            MongoMembershipUser user;
            try {                
                user = users.FindOneAs<MongoMembershipUser>(Query.EQ("_id", id));
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve user.", e);
            }
            return user;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="users"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public static MongoMembershipUser GetMongoUser(MongoCollection<MongoMembershipUser> users, string userName) {
            MongoMembershipUser user;
            try {
                user = users.FindOneAs<MongoMembershipUser>(Query.EQ("UserName", userName));
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve user.", e);
            }
            return user;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="users"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public static MongoProfile GetMongoProfile(MongoCollection<MongoMembershipUser> users, string userName) {
            MongoProfile profile;
            try {
                profile = users.Find(Query.EQ("UserName", userName))
                    
                    .Select(u => u.Profile)                    
                    .FirstOrDefault();
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve profile.", e);
            }
            return profile;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="roles"></param>
        /// <param name="roleName"></param>
        /// <returns></returns>
        public static MongoRole GetMongoRole(MongoCollection<MongoRole> roles, string roleName) {
            MongoRole role;
            try {
                role = roles.FindOneAs<MongoRole>(Query.EQ("RoleName", roleName));
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve role.", e);
            }
            return role;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessions"></param>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        public static MongoSession GetMongoSession(MongoCollection<MongoSession> sessions, string sessionId) {
            MongoSession session;
            try {
                session = sessions.FindOneAs<MongoSession>(Query.EQ("SessionId", sessionId));
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not retrieve session.", e);
            }
            return session;
        }

    }
}
