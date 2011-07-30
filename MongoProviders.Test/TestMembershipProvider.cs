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
using System.Web.Configuration;
using System.Web.Security;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoProviders.Test {
    
    [TestFixture]
    public class TestMembershipProvider {

        private const string DefaultConnectionStringName = "MongoAspNetConString";
        private const string DefaultName = "MongoMembershipProvider";

        private NameValueCollection _config;

        [TestFixtureSetUp]
        public void TestFixtureSetUp() {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);            
            
            // Add connection string.
            var connectionStringSettings = new ConnectionStringSettings(DefaultConnectionStringName, "mongodb://localhost/aspnet");
            config.ConnectionStrings.ConnectionStrings.Clear();            
            config.ConnectionStrings.ConnectionStrings.Add(connectionStringSettings); 

            // Add the provider.            
            var membership = (MembershipSection) config.GetSection("system.web/membership");
            membership.DefaultProvider = DefaultName;
            var provider = new ProviderSettings(DefaultName, typeof (MongoMembershipProvider).AssemblyQualifiedName);            
            provider.Parameters["connectionStringName"] = DefaultConnectionStringName;
            membership.Providers.Clear();
            membership.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("connectionStrings");
            ConfigurationManager.RefreshSection("system.web/membership");            

            _config = new NameValueCollection {
                { "connectionStringName", DefaultConnectionStringName },
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

        /// <summary>
        /// Tests whether a user is successfully created under normal circumstances.
        /// </summary>
        [Test]
        public void TestCreateUser() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", "Test question?", "Test answer.", true, null, out status);
            
            Assert.NotNull(createdUser);
            Assert.AreEqual("test", createdUser.UserName);
            Assert.AreEqual("test@test.com", createdUser.Email);
            Assert.AreEqual("Test question?", createdUser.PasswordQuestion);
            Assert.IsTrue(createdUser.IsApproved);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
        }

        /// <summary>
        /// Tests whether a user can be successfully created when passed a custom provider user key.
        /// </summary>
        [Test]
        public void TestCreateUserWithCustomProviderUserKey() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var providerUserKey = ObjectId.GenerateNewId();
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", "Test question?", "Test answer.", true, providerUserKey, out status);
            
            Assert.NotNull(createdUser);
            Assert.AreEqual(providerUserKey, createdUser.ProviderUserKey);
            Assert.AreEqual("test", createdUser.UserName);
            Assert.AreEqual("test@test.com", createdUser.Email);
            Assert.AreEqual("Test question?", createdUser.PasswordQuestion);
            Assert.IsTrue(createdUser.IsApproved);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
        }

        /// <summary>
        /// Tests whether a user can be retrieved using its provider user key.
        /// Test also ensures that last activity date is not updated.
        /// </summary>
        [Test]
        public void TestGetUserByProviderUserKey() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUser = provider.GetUser(createdUser.ProviderUserKey, false);
            
            Assert.NotNull(retrievedUser);
            Assert.AreEqual(createdUser.ProviderUserKey, retrievedUser.ProviderUserKey);
            Assert.AreEqual(createdUser.LastActivityDate, retrievedUser.LastActivityDate);
        }

        /// <summary>
        /// Tests whether a user can be retrieved using its provider user key with the userIsOnline parameter set to true.
        /// Test ensures the last activity date is updated.
        /// </summary>
        [Test]
        public void TestGetUserByProviderUserKeyAndSetOnline() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUser = provider.GetUser(createdUser.ProviderUserKey, true);

            Assert.NotNull(retrievedUser);
            Assert.AreEqual(createdUser.ProviderUserKey, retrievedUser.ProviderUserKey);
            Assert.LessOrEqual(createdUser.LastActivityDate, retrievedUser.LastActivityDate);
        }

        [Test]
        public void TestGetUserByProviderUserKeyWhenNonExistent() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUser = provider.GetUser(ObjectId.GenerateNewId(), false);

            Assert.IsNull(retrievedUser);
        }

        [Test]
        public void TestGetUserByUserName() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);                       
            var retrievedUser = provider.GetUser("test", false);

            Assert.NotNull(retrievedUser);
            Assert.AreEqual("test", retrievedUser.UserName);
            Assert.AreEqual(createdUser.LastActivityDate, retrievedUser.LastActivityDate);
        }

        [Test]
        public void TestGetUserByUserNameAndSetOnline() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUser = provider.GetUser("test", true);

            Assert.NotNull(retrievedUser);
            Assert.AreEqual("test", retrievedUser.UserName);
            Assert.LessOrEqual(createdUser.LastActivityDate, retrievedUser.LastActivityDate);
        }

        [Test]
        public void TestGetUserByUserNameWhenNonExistent() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUser = provider.GetUser("foo", false);

            Assert.IsNull(retrievedUser);            
        }

        [Test]
        public void TestCreateUserWithDuplicateProviderUserKey() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            var providerUserKey = ObjectId.GenerateNewId();

            MembershipCreateStatus firstStatus;
            provider.CreateUser("foo", "123456", "test@test.com", null, null, true, providerUserKey, out firstStatus);

            Assert.AreEqual(MembershipCreateStatus.Success, firstStatus);

            MembershipCreateStatus secondStatus;
            provider.CreateUser("bar", "123456", "test@test.com", null, null, true, providerUserKey, out secondStatus);
            
            Assert.AreEqual(MembershipCreateStatus.DuplicateProviderUserKey, secondStatus);
        }

        [Test]
        public void TestCreateUserWithDuplicateUserName() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus firstStatus;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out firstStatus);

            Assert.AreEqual(MembershipCreateStatus.Success, firstStatus);

            MembershipCreateStatus secondStatus;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out secondStatus);
            
            Assert.AreEqual(MembershipCreateStatus.DuplicateUserName, secondStatus);
        }

        [Test]
        public void TestGetUserNameByEmail() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            provider.CreateUser("test1", "123456", "test1@test.com", null, null, true, null, out status);
            provider.CreateUser("test2", "123456", "test2@test.com", null, null, true, null, out status);
            var retrievedUserName = provider.GetUserNameByEmail("test1@test.com");

            Assert.AreEqual("test1", retrievedUserName);
        }

        [Test]
        public void TestGetUserNameByEmailWhenNonExistent() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            provider.CreateUser("test1", "123456", "test1@test.com", null, null, true, null, out status);
            provider.CreateUser("test2", "123456", "test2@test.com", null, null, true, null, out status);
            var retrievedUserName = provider.GetUserNameByEmail("test3@test.com");

            Assert.IsNull(retrievedUserName);
        }

        [Test]
        public void TestGetUserNameByEmailWhenNonUnique() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            provider.CreateUser("bbb", "123456", "test@test.com", null, null, true, null, out status);
            provider.CreateUser("aaa", "123456", "test@test.com", null, null, true, null, out status);
            var retrievedUserName = provider.GetUserNameByEmail("test@test.com");

            Assert.AreEqual("aaa", retrievedUserName);
        }

    }

}
