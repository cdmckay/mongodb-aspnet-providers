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
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
        /// <param name="config"></param>
        /// <returns></returns>
        public static SafeMode GenerateSafeMode(NameValueCollection config) {            
            var enableFSync = Convert.ToBoolean(config["enableFSync"] ?? "false");
            var numberOfWriteReplications = Convert.ToInt32(config["numberOfWriteReplications"] ?? "0");
            var writeReplicationTimeout = Convert.ToInt32(config["writeReplicationTimeout"] ?? "0");

            return new SafeMode(true, enableFSync, numberOfWriteReplications, TimeSpan.FromMilliseconds(writeReplicationTimeout));    
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="applicationName"></param>
        /// <param name="connectionString"></param>
        /// <param name="databaseName"></param>
        /// <param name="safeMode"></param>
        public static void InitializeCollections(
            string applicationName,
            string connectionString, 
            string databaseName,
            SafeMode safeMode
        ) {
            // Initialize users collection.
            var users = GetCollectionAs<MongoMembershipUser>(applicationName, connectionString, databaseName, safeMode, "users");
            if (!users.Exists()) {
                users.ResetIndexCache();
                users.EnsureIndex(IndexKeys.Ascending("UserName"), IndexOptions.SetUnique(true));
                users.EnsureIndex(IndexKeys.Ascending("Email"));
                users.EnsureIndex(IndexKeys.Ascending("IsAnonymous"));
            }

            // Initialize roles collection.
            var roles = GetCollectionAs<MongoRole>(applicationName, connectionString, databaseName, safeMode, "roles");
            if (!roles.Exists()) {
                roles.ResetIndexCache();
                roles.EnsureIndex(IndexKeys.Ascending("RoleName"), IndexOptions.SetUnique(true));
            }

            // Initialize sessions collection.
            var sessions = GetCollectionAs<MongoSession>(applicationName, connectionString, databaseName, safeMode, "sessions");
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
        /// <param name="safeMode"></param>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public static MongoCollection<T> GetCollectionAs<T>(
            string applicationName, 
            string connectionString, 
            string databaseName,
            SafeMode safeMode,                                               
            string collectionName
        ) {            
            var server = MongoServer.Create(connectionString);
            var database = server.GetDatabase(databaseName, SafeMode.True);
            return database.GetCollection<T>(applicationName + "." + collectionName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="users"></param>
        /// <param name="id"></param>        
        /// <returns></returns>
        public static MongoMembershipUser GetMongoUser(TraceSource traceSource, MongoCollection<MongoMembershipUser> users, ObjectId id) {
            MongoMembershipUser user;
            try {                
                user = users.FindOneAs<MongoMembershipUser>(Query.EQ("_id", id));
            } catch (MongoSafeModeException e) {
                var p = new ProviderException(ProviderResources.CouldNotRetrieveUser, e);
                TraceException(traceSource, "GetMongoUser", p);
                throw p;
            }
            return user;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="users"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public static MongoMembershipUser GetMongoUser(TraceSource traceSource, MongoCollection<MongoMembershipUser> users, string userName) {
            MongoMembershipUser user;
            try {
                user = users.FindOneAs<MongoMembershipUser>(Query.EQ("UserName", userName));
            } catch (MongoSafeModeException e) {
                var p = new ProviderException(ProviderResources.CouldNotRetrieveUser, e);
                TraceException(traceSource, "GetMongoUser", p);
                throw p;
            }
            return user;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="users"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public static MongoProfile GetMongoProfile(TraceSource traceSource, MongoCollection<MongoMembershipUser> users, string userName) {
            MongoProfile profile;
            try {
                profile = users.Find(Query.EQ("UserName", userName))
                    .Select(u => u.Profile)                    
                    .FirstOrDefault();
            } catch (MongoSafeModeException e) {
                var p = new ProviderException(ProviderResources.CouldNotRetrieveProfile, e);
                TraceException(traceSource, "GetMongoProfile", p);
                throw p;
            }
            return profile;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="roles"></param>
        /// <param name="roleName"></param>
        /// <returns></returns>
        public static MongoRole GetMongoRole(TraceSource traceSource, MongoCollection<MongoRole> roles, string roleName) {
            MongoRole role;
            try {
                role = roles.FindOneAs<MongoRole>(Query.EQ("RoleName", roleName));
            } catch (MongoSafeModeException e) {
                var p = new ProviderException(ProviderResources.CouldNotRetrieveRole, e);
                TraceException(traceSource, "GetMongoRole", p);
                throw p;
            }
            return role;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="sessions"></param>
        /// <param name="id"></param>
        /// <returns></returns>
        public static MongoSession GetMongoSession(TraceSource traceSource, MongoCollection<MongoSession> sessions, string id) {
            MongoSession session;
            try {
                session = sessions.FindOneAs<MongoSession>(Query.EQ("_id", id));
            } catch (MongoSafeModeException e) {
                var p = new ProviderException(ProviderResources.CouldNotRetrieveSession, e);
                TraceException(traceSource, "GetMongoSession", p);
                throw p;
            }
            return session;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="traceSource"></param>
        /// <param name="methodName"></param>
        /// <param name="e"></param>
        public static Exception TraceException(TraceSource traceSource, string methodName, Exception e) {
            if (traceSource != null) {
                var builder = new StringBuilder();
                builder.AppendFormat("An exception occurred in the '{0}' method: {1}" + Environment.NewLine, methodName, e.Message);
                builder.Append(new StackTrace(1));
                traceSource.TraceEvent(TraceEventType.Error, 0, builder.ToString().TrimEnd('\r', '\n'));
            }
            return e;
        }

    }
}
