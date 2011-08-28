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
                var update = Update.Set("Profile.LastActivityDate", DateTime.Now);
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
                    case SettingsSerializeAs.Binary:
                        value.SerializedValue = profile.Properties[p.Name];
                        break;
                    case SettingsSerializeAs.ProviderSpecific:
                        value.PropertyValue = profile.Properties[p.Name];
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

            // Create the profile BSON document.
            var profile = new BsonDocument();
            var profileWriterSettings = new BsonDocumentWriterSettings();
            var writer = new BsonDocumentWriter(profile, profileWriterSettings);
            writer.WriteStartDocument();
            foreach (var value in updateValues) {                                
                writer.WriteName(value.Name);
                switch (value.Property.SerializeAs) {
                    case SettingsSerializeAs.String:
                    case SettingsSerializeAs.Xml:                    
                        BsonSerializer.Serialize(writer, typeof (string), value.SerializedValue);
                        break;
                    case SettingsSerializeAs.Binary:
                        BsonSerializer.Serialize(writer, typeof (byte[]), value.SerializedValue);
                        break;
                    case SettingsSerializeAs.ProviderSpecific:
                        BsonSerializer.Serialize(writer, value.Property.PropertyType, value.PropertyValue);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }                
            }
            writer.WriteEndDocument();

            try {
                var query = Query.EQ("UserName", userName);
                var update = Update                    
                    .Set("Profile.Properties", profile)
                    .Set("Profile.LastActivityDate", DateTime.Now)
                    .Set("Profile.LastUpdateDate", DateTime.Now);                  

                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not update profile.", e);
            }
        }

        public override int DeleteProfiles(ProfileInfoCollection profiles) {
            throw new NotImplementedException();
        }

        public override int DeleteProfiles(string[] usernames) {
            throw new NotImplementedException();
        }

        public override int DeleteInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            throw new NotImplementedException();
        }

        public override int GetNumberOfInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection GetAllProfiles(ProfileAuthenticationOption authenticationOption, int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection GetAllInactiveProfiles(ProfileAuthenticationOption authenticationOption, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection FindProfilesByUserName(ProfileAuthenticationOption authenticationOption, string usernameToMatch, int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }

        public override ProfileInfoCollection FindInactiveProfilesByUserName(ProfileAuthenticationOption authenticationOption, string usernameToMatch, DateTime userInactiveSinceDate, int pageIndex, int pageSize, out int totalRecords) {
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
