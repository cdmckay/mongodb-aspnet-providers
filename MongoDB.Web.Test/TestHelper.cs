using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Security.Cryptography;
using System.Web.Configuration;
using DigitalLiberationFront.MongoDB.Web.Security;

namespace DigitalLiberationFront.MongoDB.Web.Test {
    public static class TestHelper {

        private const string ConnectionStringName = "MongoAspNetConString";
        public const string DefaultMembershipName = "MongoMembershipProvider";
        public const string DefaultRoleName = "MongoRoleProvider";

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

    }
}
