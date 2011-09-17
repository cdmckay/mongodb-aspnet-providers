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
using System.Configuration;
using System.Configuration.Provider;
using System.Diagnostics;
using System.Linq;
using System.Web.Profile;
using DigitalLiberationFront.MongoDB.Web.Resources;
using DigitalLiberationFront.MongoDB.Web.Security;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Profile {
    public class MongoProfileProvider : ProfileProvider {

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
                config["description"] = "MongoDB Profile Provider";
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

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties) {
            if (context == null) {
                throw TraceException("GetPropertyValues", new ArgumentNullException("context"));
            }
            if (properties == null) {
                throw TraceException("GetPropertyValues", new ArgumentNullException("properties"));
            }

            if (properties.Count == 0) {
                return new SettingsPropertyValueCollection();
            }

            var userName = (string) context["UserName"];
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("GetPropertyValues", new ProviderException(message));
            }

            var profile = GetMongoProfile(userName);
            try {
                var query = Query.EQ("UserName", userName);
                var update = Update.Set("Profile.LastActivityDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotUpdateProfile;
                throw TraceException("GetPropertyValues", new ProviderException(message, e));
            }

            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty p in properties) {
                var value = new SettingsPropertyValue(p);
                switch (value.Property.SerializeAs) {
                    case SettingsSerializeAs.String:                        
                    case SettingsSerializeAs.Xml:
                        value.SerializedValue = profile.Properties[p.Name].AsString;
                        break;
                    case SettingsSerializeAs.Binary:
                        value.SerializedValue = profile.Properties[p.Name].AsByteArray;
                        break;
                    case SettingsSerializeAs.ProviderSpecific:
                        value.PropertyValue = profile.Properties[p.Name].RawValue;
                        break;
                    default:
                        throw TraceException("GetPropertyValues", new ArgumentOutOfRangeException());
                }
                values.Add(value);
            }           
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values) {
            if (context == null) {
                throw TraceException("SetPropertyValues", new ArgumentNullException("context"));
            }
            if (values == null) {
                throw TraceException("SetPropertyValues", new ArgumentNullException("values"));
            }

            var userName = (string) context["UserName"];
            var isAuthenticated = (bool) context["IsAuthenticated"];

            if (string.IsNullOrWhiteSpace(userName) || values.Count == 0) {
                return;
            }

            var updateValues = (from SettingsPropertyValue value in values
                               let allowAnonymous = value.Property.Attributes["AllowAnonymous"].Equals(true)                               
                               where (value.IsDirty || !value.UsingDefaultValue) && (isAuthenticated || allowAnonymous)
                               select value).ToList();
            
            // If there are no values to update, then we're done here.
            if (updateValues.Count == 0) {
                return;
            }

            // If the user doesn't exist, and it's anonymous, create it.
            var user = GetMongoUser(userName);
            if (user == null) {
                if (!isAuthenticated) {
                    user = new MongoMembershipUser {
                        UserName = userName,
                        IsAnonymous = true,
                        CreationDate = DateTime.Now,
                    };

                    try {
                        var users = GetUserCollection();
                        users.Insert(user);
                    }
                    catch (MongoSafeModeException e) {
                        var message = ProviderResources.CouldNotCreateUser;
                        throw TraceException("SetPropertyValues", new ProviderException(message, e));
                    }
                } else {
                    var message = ProviderResources.CouldNotFindUser;
                    throw TraceException("SetPropertyValues", new ProviderException(message));
                }
            }

            // Create the properties BSON document.
            var properties = new BsonDocument();
            var propertiesWriterSettings = new BsonDocumentWriterSettings();
            var propertiesWriter = new BsonDocumentWriter(properties, propertiesWriterSettings);
            propertiesWriter.WriteStartDocument();            
            foreach (var value in updateValues) {                                
                propertiesWriter.WriteName(value.Name);
                switch (value.Property.SerializeAs) {
                    case SettingsSerializeAs.String:
                    case SettingsSerializeAs.Xml:                    
                        BsonSerializer.Serialize(propertiesWriter, typeof (string), value.SerializedValue);
                        break;
                    case SettingsSerializeAs.Binary:
                        BsonSerializer.Serialize(propertiesWriter, typeof (byte[]), value.SerializedValue);
                        break;
                    case SettingsSerializeAs.ProviderSpecific:
                        BsonSerializer.Serialize(propertiesWriter, value.Property.PropertyType, value.PropertyValue);
                        break;
                    default:
                        throw TraceException("SetPropertyValues", new ArgumentOutOfRangeException());
                }                
            }
            propertiesWriter.WriteEndDocument();

            // Create the profile BSON document.
            var profile = SerializationHelper.Serialize(typeof (MongoProfile), new MongoProfile {
                Properties = properties,
                LastActivityDate = DateTime.Now,
                LastUpdateDate = DateTime.Now
            });

            try {
                var query = Query.EQ("UserName", userName);
                var update = Update.Set("Profile", profile);
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotUpdateProfile;
                throw TraceException("SetPropertyValues", new ProviderException(message, e));
            }
        }

        public override int DeleteProfiles(ProfileInfoCollection profiles) {
            if (profiles == null) {
                throw TraceException("DeleteProfiles", new ArgumentNullException("profiles"));
            }
            if (profiles.Count == 0) {
                return 0;
            }

            return DeleteProfiles(profiles.Cast<ProfileInfo>().Select(p => p.UserName).ToArray());
        }

        public override int DeleteProfiles(string[] userNames) {
            if (userNames == null) {
                throw TraceException("DeleteProfiles", new ArgumentNullException("userNames"));
            }
            if (userNames.Length == 0) {
                return 0;
            }

            try {
                var query = Query.And(
                    Query.Exists("Profile", true), 
                    Query.In("UserName", BsonArray.Create(userNames.AsEnumerable())));                                       
                var update = Update.Unset("Profile");
                var users = GetUserCollection();
                var result = users.Update(query, update, UpdateFlags.Multi);
                return result.DocumentsAffected;
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRemoveProfiles;
                throw TraceException("DeleteProfiles", new ProviderException(message, e));
            }
        }

        public override int DeleteInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            var authenticationQuery = ConvertProfileAuthenticationOptionToMongoQuery(authenticationOption);
            
            try {
                var query = Query.And(
                    Query.Exists("Profile", true), authenticationQuery,
                    Query.LTE("Profile.LastActivityDate.Ticks", userInactiveSinceDate.Ticks));
                var update = Update.Unset("Profile");
                var users = GetUserCollection();
                var result = users.Update(query, update, UpdateFlags.Multi);
                return result.DocumentsAffected;
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRemoveProfiles;
                throw TraceException("DeleteInactiveProfiles", new ProviderException(message, e));
            }
        }

        public override int GetNumberOfInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            var authenticationQuery = ConvertProfileAuthenticationOptionToMongoQuery(authenticationOption);

            try {
                var query = Query.And(
                    Query.Exists("Profile", true), authenticationQuery,
                    Query.LTE("Profile.LastActivityDate.Ticks", userInactiveSinceDate.Ticks));
                var users = GetUserCollection();
                return users.Count(query);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotCountProfiles;
                throw TraceException("GetNumberOfInactiveProfiles", new ProviderException(message, e));
            }
        }

        public override ProfileInfoCollection GetAllProfiles(ProfileAuthenticationOption authenticationOption, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllProfiles", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllProfiles", new ArgumentException(message, "pageSize"));
            }

            var profiles = FindProfiles(authenticationOption, Query.Null, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public override ProfileInfoCollection GetAllInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllInactiveProfiles", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllInactiveProfiles", new ArgumentException(message, "pageSize"));
            }

            var query = Query.LTE("Profile.LastActivityDate.Ticks", userInactiveSinceDate.Ticks);
            var profiles = FindProfiles(authenticationOption, query, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public override ProfileInfoCollection FindProfilesByUserName(ProfileAuthenticationOption authenticationOption, string userNameToMatch, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindProfilesByUserName", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindProfilesByUserName", new ArgumentException(message, "pageSize"));
            }

            var query = Query.Matches("UserName", userNameToMatch);
            var profiles = FindProfiles(authenticationOption, query, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public override ProfileInfoCollection FindInactiveProfilesByUserName(ProfileAuthenticationOption authenticationOption, string userNameToMatch, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindInactiveProfilesByUserName", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindInactiveProfilesByUserName", new ArgumentException(message, "pageSize"));
            }

            var query = Query.And(
                Query.Matches("UserName", userNameToMatch),
                Query.LTE("Profile.LastActivityDate.Ticks", userInactiveSinceDate.Ticks));
            var profiles = FindProfiles(authenticationOption, query, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public virtual IEnumerable<ProfileInfo> FindProfiles(ProfileAuthenticationOption authenticationOption, IMongoQuery query, IMongoSortBy sortBy, int skip, int take, out int totalRecords) {
            if (skip < 0) {
                var message = ProviderResources.SkipMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindProfiles", new ArgumentException(message, "skip"));
            }
            if (take < 0) {
                var message = ProviderResources.TakeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindProfiles", new ArgumentException(message, "take"));
            }

            var authenticationQuery = ConvertProfileAuthenticationOptionToMongoQuery(authenticationOption);
            var modifiedQuery = Query.And(Query.Exists("Profile", true), authenticationQuery, query);
            var users = GetUserCollection();
            var matches = users.Find(modifiedQuery).SetSkip(skip).SetLimit(take);
            if (sortBy != null) {
                matches.SetSortOrder(sortBy);
            }
            totalRecords = matches.Count();
            return matches.Select(u => new ProfileInfo(
                u.UserName, 
                u.IsAnonymous, 
                u.Profile.LastActivityDate, 
                u.Profile.LastUpdateDate, 
                u.Profile.ToBson().Length));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="authenticationOption"></param>
        /// <returns></returns>
        private IMongoQuery ConvertProfileAuthenticationOptionToMongoQuery(ProfileAuthenticationOption authenticationOption) {
            IMongoQuery query;
            switch (authenticationOption) {
                case ProfileAuthenticationOption.Anonymous:
                    query = Query.EQ("IsAnonymous", true);
                    break;
                case ProfileAuthenticationOption.Authenticated:
                    query = Query.EQ("IsAnonymous", false);
                    break;
                case ProfileAuthenticationOption.All:
                    query = Query.Null;
                    break;
                default:
                    throw TraceException("ConvertProfileAuthenticationOptionToMongoQuery", 
                        new ArgumentOutOfRangeException("authenticationOption"));
            }
            return query;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="profiles"></param>
        /// <returns></returns>
        private static ProfileInfoCollection ConvertProfileInfoEnumerableToCollection(IEnumerable<ProfileInfo> profiles) {
            var collection = new ProfileInfoCollection();
            foreach (var p in profiles) {
                collection.Add(p);
            }
            return collection;
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
        private MongoProfile GetMongoProfile(string userName) {
            return ProviderHelper.GetMongoProfile(_traceSource, GetUserCollection(), userName);
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
