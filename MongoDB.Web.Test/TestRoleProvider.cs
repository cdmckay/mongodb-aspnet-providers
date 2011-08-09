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
using System.Configuration;
using System.Configuration.Provider;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Configuration;
using System.Web.Security;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoDB.Web.Security.Test {

    [TestFixture]
    public class TestRoleProvider {

        private const string DefaultConnectionStringName = "MongoAspNetConString";
        private const string DefaultName = "MongoRoleProvider";

        private NameValueCollection _config;

        #region Test SetUp and TearDown

        [TestFixtureSetUp]
        public void TestFixtureSetUp() {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Add connection string.
            var connectionStringSettings = new ConnectionStringSettings(DefaultConnectionStringName,
                                                                        "mongodb://localhost/aspnet");
            config.ConnectionStrings.ConnectionStrings.Clear();
            config.ConnectionStrings.ConnectionStrings.Add(connectionStringSettings);

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
            var roleManager = (RoleManagerSection) config.GetSection("system.web/roleManager");
            roleManager.DefaultProvider = DefaultName;
            var provider = new ProviderSettings(DefaultName, typeof(MongoRoleProvider).AssemblyQualifiedName);
            provider.Parameters["connectionStringName"] = DefaultConnectionStringName;
            roleManager.Providers.Clear();
            roleManager.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("connectionStrings");
            ConfigurationManager.RefreshSection("system.web/machineKey");
            ConfigurationManager.RefreshSection("system.web/roleManager");

            _config = new NameValueCollection {
                {"connectionStringName", DefaultConnectionStringName}
            };
        }

        [TestFixtureTearDown]
        public void TestFixtureTearDown() {

        }

        [SetUp]
        public void SetUp() {
            var server = MongoServer.Create("mongodb://localhost/aspnet");
            var database = server.GetDatabase("aspnet");
            database.Drop();
        }

        #endregion

        #region Initialize

        [Test]
        public void TestInitializeWhenCalledTwice() {
            var config = new NameValueCollection(_config);

            var provider = new MongoRoleProvider();
            Assert.Throws<InvalidOperationException>(() => {
                provider.Initialize(DefaultName, config);
                provider.Initialize(DefaultName, config);
            });
        }      

        #endregion    

        #region CreateRole

        [Test]
        public void TestCreateRole() {
            var config = new NameValueCollection(_config);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultName, config);
            
            Assert.IsFalse(provider.RoleExists("test"));
            provider.CreateRole("test");
            Assert.IsTrue(provider.RoleExists("test"));
        }

        [Test]
        public void TestCreateRoleWithDuplicateRoleName() {
            var config = new NameValueCollection(_config);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultName, config);

            provider.CreateRole("test");
            Assert.Throws<ProviderException>(() => provider.CreateRole("test"));
        }

        [Test]
        public void TestCreateRoleWithCommaRoleName() {
            var config = new NameValueCollection(_config);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultName, config);

            Assert.Throws<ArgumentException>(() => provider.CreateRole("test,"));
        }

        #endregion

    }

}
