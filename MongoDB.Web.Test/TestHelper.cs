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
using System.Security.Cryptography;
using System.Web.Configuration;
using DigitalLiberationFront.MongoDB.Web.Profile;
using DigitalLiberationFront.MongoDB.Web.Security;

namespace DigitalLiberationFront.MongoDB.Web.Test {
    public static class TestHelper {

        private const string ConnectionStringName = "MongoAspNetConString";
        public const string DefaultMembershipName = "MongoMembershipProvider";
        public const string DefaultRoleName = "MongoRoleProvider";
        public const string DefaultProfileName = "MongoProfileProvider";

        public static void ConfigureConnectionStrings() {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Add connection string.            
            var connectionStringSettings = new ConnectionStringSettings(ConnectionStringName,
                                                                        "mongodb://localhost/aspnet");
            config.ConnectionStrings.ConnectionStrings.Clear();
            config.ConnectionStrings.ConnectionStrings.Add(connectionStringSettings);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("connectionStrings");
        }

        public static NameValueCollection ConfigureMembershipProvider(string name) {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Add machine keys (for encrypted passwords).                                    
            var rng = new RNGCryptoServiceProvider();

            var validationKeyBuffer = new byte[64];
            rng.GetBytes(validationKeyBuffer);
            var validationKey = BitConverter.ToString(validationKeyBuffer).Replace("-", string.Empty);

            var decryptionKeyBuffer = new byte[32];
            rng.GetBytes(decryptionKeyBuffer);
            var decryptionKey = BitConverter.ToString(decryptionKeyBuffer).Replace("-", string.Empty);

            var machineKey = (MachineKeySection) config.GetSection("system.web/machineKey");
            machineKey.ValidationKey = validationKey;
            machineKey.Validation = MachineKeyValidation.SHA1;
            machineKey.DecryptionKey = decryptionKey;
            machineKey.Decryption = "AES";

            // Add the provider.           
            var membership = (MembershipSection) config.GetSection("system.web/membership");
            membership.DefaultProvider = name;
            var provider = new ProviderSettings(name, typeof(MongoMembershipProvider).AssemblyQualifiedName);
            provider.Parameters["connectionStringName"] = ConnectionStringName;
            membership.Providers.Clear();
            membership.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("system.web/machineKey");
            ConfigurationManager.RefreshSection("system.web/membership");

            return new NameValueCollection {
                {"connectionStringName", ConnectionStringName},
                {"minRequiredNonAlphanumericCharacters", "0"},
                {"requiresUniqueEmail", "false"}
            };
        }

        public static NameValueCollection ConfigureRoleProvider(string name) {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Add the provider.            
            var roleManager = (RoleManagerSection) config.GetSection("system.web/roleManager");
            roleManager.DefaultProvider = DefaultRoleName;

            var provider = new ProviderSettings(DefaultRoleName, typeof(MongoRoleProvider).AssemblyQualifiedName);
            provider.Parameters["connectionStringName"] = ConnectionStringName;
            roleManager.Providers.Clear();
            roleManager.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("system.web/roleManager");

            return new NameValueCollection {
                {"connectionStringName", ConnectionStringName}
            };
        }

        public static NameValueCollection ConfigureProfileProvider(string name) {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Add the provider.            
            var profile = (ProfileSection) config.GetSection("system.web/profile");
            profile.DefaultProvider = DefaultProfileName;

            var provider = new ProviderSettings(DefaultProfileName, typeof(MongoProfileProvider).AssemblyQualifiedName);
            provider.Parameters["connectionStringName"] = ConnectionStringName;
            profile.Providers.Clear();
            profile.Providers.Add(provider);
            
            profile.PropertySettings.Clear();

            // For testing regular string properties.
            profile.PropertySettings.Add(new ProfilePropertySettings("firstName"));
            profile.PropertySettings.Add(new ProfilePropertySettings("lastName"));

            // For testing regualr DateTime properties.
            profile.PropertySettings.Add(new ProfilePropertySettings("birthDate"));

            // For testing anonymous settings.
            profile.PropertySettings.Add(new ProfilePropertySettings("clickCount") { AllowAnonymous = true });

            // For testing read-only.
            // For testing default value.
            // For testing forcing string serialization.
            // For testing forcing XML serialization.
            // For testing forcing byte[] serialization.           

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("system.web/profile");

            return new NameValueCollection {
                {"connectionStringName", ConnectionStringName}
            };
        }

        public static SettingsContext GenerateSettingsContext(string userName, bool isAuthenticated) {
            return new SettingsContext {
                { "UserName", userName },
                { "IsAuthenticated", isAuthenticated },
            };
        }

        //public static SettingsPropertyValueCollection GenerateSettingsPropertyValueCollection(IDictionary<string, object> map) {
        //    var collection = new SettingsPropertyValueCollection();
        //    foreach (var pair in map) {
        //        var property = new SettingsProperty()
        //        collection.Add(new SettingsPropertyValue());
        //    }
        //    return collection;
        //}

    }
}
