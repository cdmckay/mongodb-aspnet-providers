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
using System.Linq;
using System.Text;
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

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection properties) {
            if (context == null) {
                throw new ArgumentNullException("context");
            }
            if (properties == null) {
                throw new ArgumentNullException("properties");
            }

            if (properties.Count == 0) {
                return new SettingsPropertyValueCollection();
            }

            var userName = (string) context["UserName"];
            if (string.IsNullOrWhiteSpace(userName)) {
                throw new ProviderException(ProviderResources.Membership_UserNameCannotBeNullOrWhiteSpace);
            }

            var profile = GetMongoProfile(userName);
            try {
                var query = Query.EQ("UserName", userName);
                var update = Update.Set("Profile.LastActivityDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not update last activity date for profile.", e);
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
                        throw new ArgumentOutOfRangeException();
                }
                values.Add(value);
            }           
            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection values) {
            if (context == null) {
                throw new ArgumentNullException("context");
            }
            if (values == null) {
                throw new ArgumentNullException("values");
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
                        throw new ProviderException("Could not create anonymous user.", e);
                    }
                } else {
                    throw new ProviderException("User was authenticated but could not be found.");    
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
                        throw new ArgumentOutOfRangeException();
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
                throw new ProviderException("Could not update profile.", e);
            }
        }

        public override int DeleteProfiles(ProfileInfoCollection profiles) {
            throw new NotImplementedException();
        }

        public override int DeleteProfiles(string[] userNames) {
            throw new NotImplementedException();
        }

        public override int DeleteInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            throw new NotImplementedException();
        }

        public override int GetNumberOfInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection GetAllProfiles(ProfileAuthenticationOption authenticationOption, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                throw new ArgumentException(ProviderResources.Common_PageIndexMustBeGreaterThanOrEqualToZero, "pageIndex");
            }
            if (pageSize < 0) {
                throw new ArgumentException(ProviderResources.Common_PageSizeMustBeGreaterThanOrEqualToZero, "pageSize");
            }

            var profiles = FindProfiles(authenticationOption, Query.Null, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public override ProfileInfoCollection GetAllInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                throw new ArgumentException(ProviderResources.Common_PageIndexMustBeGreaterThanOrEqualToZero, "pageIndex");
            }
            if (pageSize < 0) {
                throw new ArgumentException(ProviderResources.Common_PageSizeMustBeGreaterThanOrEqualToZero, "pageSize");
            }

            var query = Query.LTE("Profile.LastActivityDate.Ticks", userInactiveSinceDate.Ticks);
            var profiles = FindProfiles(authenticationOption, query, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertProfileInfoEnumerableToCollection(profiles);
        }

        public override ProfileInfoCollection FindProfilesByUserName(ProfileAuthenticationOption authenticationOption, string userNameToMatch, int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection FindInactiveProfilesByUserName(ProfileAuthenticationOption authenticationOption, string userNameToMatch, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }

        public virtual IEnumerable<ProfileInfo> FindProfiles(ProfileAuthenticationOption authenticationOption, IMongoQuery query, IMongoSortBy sortBy, int skip, int take, out int totalRecords) {
            if (skip < 0) {
                throw new ArgumentException(ProviderResources.Common_SkipMustBeGreaterThanOrEqualToZero, "skip");
            }
            if (take < 0) {
                throw new ArgumentException(ProviderResources.Common_TakeMustBeGreaterThanOrEqualToZero, "take");
            }
            
            IMongoQuery authenticationQuery;
            switch (authenticationOption) {
                case ProfileAuthenticationOption.Anonymous:
                    authenticationQuery = Query.EQ("IsAnonymous", true);
                    break;
                case ProfileAuthenticationOption.Authenticated:
                    authenticationQuery = Query.EQ("IsAnonymous", false);
                    break;
                case ProfileAuthenticationOption.All:
                    authenticationQuery = Query.Null;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("authenticationOption");
            }

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
            return ProviderHelper.GetCollectionAs<MongoMembershipUser>(ApplicationName, _connectionString, _databaseName, "users");
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
        private MongoProfile GetMongoProfile(string userName) {
            return ProviderHelper.GetMongoProfile(GetUserCollection(), userName);
        }

    }
}
