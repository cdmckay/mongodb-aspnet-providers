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
using System.Web;
using System.Web.Configuration;
using System.Web.Hosting;
using System.Web.SessionState;
using DigitalLiberationFront.MongoDB.Web.Resources;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.SessionState {
    public class MongoSessionStateStore : SessionStateStoreProviderBase {

        private int _timeout;
        private string _applicationName;
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
                config["description"] = "MongoDB Session State Store";
            }

            // Initialize the base class.
            base.Initialize(name, config);            

            // Deal with the application name.           
            _applicationName = ProviderHelper.ResolveApplicationName(config);

            // Get the connection string.
            _connectionString = ProviderHelper.ResolveConnectionString(config);

            // Get the database name.
            var mongoUrl = new MongoUrl(_connectionString);
            _databaseName = mongoUrl.DatabaseName;

            _safeMode = ProviderHelper.GenerateSafeMode(config);

            // Initialize collections.
            ProviderHelper.InitializeCollections(_applicationName, _connectionString, _databaseName, _safeMode);

            // Get the timeout value.
            var webConfig = WebConfigurationManager.OpenWebConfiguration(HostingEnvironment.ApplicationVirtualPath);
            var sessionStateSection = (SessionStateSection) webConfig.GetSection("system.web/sessionState");
            _timeout = sessionStateSection.Timeout.Minutes;
        }

        public override void InitializeRequest(HttpContext context) {
        }

        public override void EndRequest(HttpContext context) {         
        }

        public override void Dispose() {
        }

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout) {
            var newSession = new MongoSession {
                Id = id,
                CreatedDate = DateTime.Now,
                ExpiresDate = DateTime.Now.AddMinutes(timeout),                
                LockId = ObjectId.Empty,
                LockedDate = DateTime.Now,
                IsLocked = false,
                Timeout = timeout,
                Properties = null,
                Actions = SessionStateActions.InitializeItem
            };

            try {
                var sessions = GetSessionCollection();
                sessions.Insert(newSession);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotCreateSession, e);
            }
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback) {
            return false;
        }

        public override SessionStateStoreData GetItem(
            HttpContext context, 
            string id, 
            out bool locked, 
            out TimeSpan lockAge, 
            out object lockId, 
            out SessionStateActions actions
        ) {
            return GetItemOptionalExclusive(false, context, id, out locked, out lockAge, out lockId, out actions);
        }

        public override SessionStateStoreData GetItemExclusive(
            HttpContext context, 
            string id, 
            out bool locked, 
            out TimeSpan lockAge, 
            out object lockId, 
            out SessionStateActions actions
        ) {
            return GetItemOptionalExclusive(true, context, id, out locked, out lockAge, out lockId, out actions);
        }

        private SessionStateStoreData GetItemOptionalExclusive(
            bool exclusive,
            HttpContext context,
            string id,
            out bool locked,
            out TimeSpan lockAge,
            out object lockId,
            out SessionStateActions actions
        ) {
            var sessions = GetSessionCollection();

            // Obtain lock if this is an exclusive call.
            bool lockAcquired = false;
            if (exclusive) {
                try {
                    var query = Query.And(
                        Query.EQ("_id", id),
                        Query.EQ("IsLocked", false),
                        Query.GT("ExpiresDate.Ticks", DateTime.Now.Ticks));
                    var update = Update
                        .Set("IsLocked", true)
                        .Set("LockedDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                    var result = sessions.Update(query, update);
                    lockAcquired = result.DocumentsAffected == 1;
                } catch (MongoSafeModeException e) {
                    throw new ProviderException(ProviderResources.CouldNotUpdateSession, e);
                }
            }

            // Retrieve the session.
            var session = GetMongoSession(id);

            // Make sure it exists and has not expired.
            bool sessionHasExpired = session != null && DateTime.Now > session.ExpiresDate;
            if (session == null || sessionHasExpired) {
                locked = false;
                lockAge = TimeSpan.Zero;
                lockId = ObjectId.Empty;
                actions = SessionStateActions.None;                

                if (sessionHasExpired) {
                    try {
                        var query = Query.EQ("Id", id);
                        sessions.Remove(query);
                    } catch (MongoSafeModeException e) {
                        throw new ProviderException(ProviderResources.CouldNotRemoveSession, e);
                    }
                }

                return null;
            }            

            // Make sure it is not locked or that we acquired a lock.
            if (!lockAcquired && session.IsLocked) {
                locked = true;
                lockAge = DateTime.Now.Subtract(session.LockedDate);
                lockId = session.LockId;
                actions = SessionStateActions.None;

                return null;
            }

            // Getting here means that the record was either unlocked,
            // or a lock was acquired.
            locked = false;
            lockAge = DateTime.Now.Subtract(session.LockedDate);
            lockId = ObjectId.GenerateNewId();
            actions = session.Actions;

            try {
                var query = Query.EQ("_id", id);
                var update = Update
                    .Set("LockId", (ObjectId) lockId)
                    .Set("Actions", SessionStateActions.None);
                sessions.Update(query, update);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotUpdateSession, e);
            }

            SessionStateStoreData storeData;
            switch (actions) {
                case SessionStateActions.None:
                    storeData = ConvertBsonDocumentToStoreData(context, session.Timeout, session.Properties);
                    break;
                case SessionStateActions.InitializeItem:
                    storeData = CreateNewStoreData(context, session.Timeout);
                    break;
                default:
                    throw new ArgumentOutOfRangeException("actions");
            }

            return storeData;
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId) {
            if (!(lockId is ObjectId)) {
                throw new ArgumentException(ProviderResources.LockIdMustBeAnObjectId, "lockId");
            }

            try {
                var sessions = GetSessionCollection();
                var query = Query.And(
                    Query.EQ("_id", id),
                    Query.EQ("LockId", (ObjectId) lockId));
                var expiresDate = SerializationHelper.SerializeDateTime(DateTime.Now.AddMinutes(_timeout));
                var update = Update
                    .Set("ExpiresDate", expiresDate)
                    .Set("IsLocked", false);
                sessions.Update(query, update);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotUpdateSession, e);
            }
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData storeData, object lockId, bool newStoreData) {
            if (!(lockId is ObjectId)) {
                throw new ArgumentException(ProviderResources.LockIdMustBeAnObjectId, "lockId");
            }

            var sessions = GetSessionCollection();
            if (newStoreData) {
                // Ensure expired session is removed first, if it exists.
                try {
                    sessions.Remove(Query.EQ("_id", id));
                    var session = new MongoSession {
                        Id = id,
                        CreatedDate = DateTime.Now,
                        ExpiresDate = DateTime.Now.AddMinutes(storeData.Timeout),
                        LockId = ObjectId.Empty,
                        LockedDate = DateTime.Now,
                        IsLocked = false,
                        Timeout = storeData.Timeout,
                        Properties = ConvertStoreDataToBsonDocument(storeData),
                        Actions = SessionStateActions.None
                    };
                    sessions.Insert(session);
                } catch (MongoSafeModeException e) {
                    throw new ProviderException(ProviderResources.CouldNotCreateSession, e);
                }
            } else {
                try {
                    var query = Query.And(
                        Query.EQ("_id", id),
                        Query.EQ("LockId", (ObjectId) lockId));
                    var expiresDate = SerializationHelper.SerializeDateTime(DateTime.Now.AddMinutes(storeData.Timeout));
                    var update = Update
                        .Set("ExpiresDate", expiresDate)
                        .Set("IsLocked", false)
                        .Set("Properties", ConvertStoreDataToBsonDocument(storeData));
                    sessions.Update(query, update);
                } catch (MongoSafeModeException e) {
                    throw new ProviderException(ProviderResources.CouldNotUpdateSession, e);
                }
            }
        }        

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData storeData) {
            if (!(lockId is ObjectId)) {
                throw new ArgumentException(ProviderResources.LockIdMustBeAnObjectId, "lockId");
            }

            try {
                var sessions = GetSessionCollection();
                var query = Query.And(
                    Query.EQ("_id", id),
                    Query.EQ("LockId", (ObjectId) lockId));                
                sessions.Remove(query);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotRemoveSession, e);
            }
        }

        public override void ResetItemTimeout(HttpContext context, string id) {
            try {
                var sessions = GetSessionCollection();
                var query = Query.EQ("_id", id);
                var expiresDate = SerializationHelper.SerializeDateTime(DateTime.Now.AddMinutes(_timeout));
                var update = Update.Set("ExpiresDate", expiresDate);
                sessions.Update(query, update);
            } catch (MongoSafeModeException e) {
                throw new ProviderException(ProviderResources.CouldNotUpdateSession, e);
            }
        }

        public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout) {
            return new SessionStateStoreData(
                new SessionStateItemCollection(), 
                SessionStateUtility.GetSessionStaticObjects(context), 
                timeout);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="timeout"></param>
        /// <param name="document"></param>
        /// <returns></returns>
        private static SessionStateStoreData ConvertBsonDocumentToStoreData(HttpContext context, int timeout, BsonDocument document) {
            if (document == null) {
                document = new BsonDocument();
            }

            var collection = new SessionStateItemCollection();
            foreach (var field in document) {
                collection[field.Name] = field.Value.RawValue;
            }
            return new SessionStateStoreData(
                collection,
                SessionStateUtility.GetSessionStaticObjects(context),
                timeout);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="storeData"></param>
        /// <returns></returns>
        private static BsonDocument ConvertStoreDataToBsonDocument(SessionStateStoreData storeData) {
            var items = storeData.Items;
            var document = new BsonDocument();
            var documentWriterSettings = new BsonDocumentWriterSettings();
            var documentWriter = new BsonDocumentWriter(document, documentWriterSettings);
            documentWriter.WriteStartDocument();
            foreach (string key in items.Keys) {
                var value = items[key];
                documentWriter.WriteName(key);
                BsonSerializer.Serialize(documentWriter, value.GetType(), value);                
            }
            documentWriter.WriteEndDocument();
            return document;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private MongoCollection<MongoSession> GetSessionCollection() {
            return ProviderHelper.GetCollectionAs<MongoSession>(_applicationName, _connectionString, _databaseName, _safeMode, "sessions");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sessionId"></param>
        /// <returns></returns>
        private MongoSession GetMongoSession(string sessionId) {
            return ProviderHelper.GetMongoSession(GetSessionCollection(), sessionId);
        }

    }
}
