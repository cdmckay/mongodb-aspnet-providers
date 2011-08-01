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
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Configuration;
using System.Web.Security;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
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
            membership.DefaultProvider = DefaultName;
            var provider = new ProviderSettings(DefaultName, typeof (MongoMembershipProvider).AssemblyQualifiedName);            
            provider.Parameters["connectionStringName"] = DefaultConnectionStringName;
            membership.Providers.Clear();
            membership.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("connectionStrings");
            ConfigurationManager.RefreshSection("system.web/machineKey");
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

        private void TestChangePasswordQuestionAndAnswerWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);

            var changed = provider.ChangePasswordQuestionAndAnswer("test", "123456", "Question", "Answer");
            Assert.IsTrue(changed);

            var user = provider.GetUser("test", false);
            Assert.IsNotNull(user);
            Assert.AreEqual("Question", user.PasswordQuestion);
            // TODO Check answer when GetPassword implemented.
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithPasswordFormatClear() {
            TestChangePasswordQuestionAndAnswerWithPasswordFormat("clear");
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithPasswordFormatHashed() {
            TestChangePasswordQuestionAndAnswerWithPasswordFormat("hashed");
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithPasswordFormatEncrypted() {
            TestChangePasswordQuestionAndAnswerWithPasswordFormat("encrypted");
        }

        /// <summary>
        /// Tests if the provider validates a user using the given password format.
        /// </summary>
        /// <param name="passwordFormat"></param>
        private void TestValidateUserWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);

            var validated = provider.ValidateUser("test", "123456");
            Assert.IsTrue(validated);
        }

        [Test]
        public void TestValidateUserWithPasswordFormatClear() {
            TestValidateUserWithPasswordFormat("clear");
        }

        [Test]
        public void TestValidateUserWithPasswordFormatHashed() {
            TestValidateUserWithPasswordFormat("hashed");
        }

        [Test]
        public void TestValidateUserWithPasswordFormatEncrypted() {
            TestValidateUserWithPasswordFormat("encrypted");
        }

        /// <summary>
        /// Tests if the provider invalidates a user using the given password format.
        /// </summary>
        /// <param name="passwordFormat"></param>
        private void TestValidateUserWithWrongPasswordWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);

            var validated = provider.ValidateUser("test", "XXXXXX");
            Assert.IsFalse(validated);
        }

        [Test]
        public void TestValidateUserWithWrongPasswordWithPasswordFormatClear() {
            TestValidateUserWithWrongPasswordWithPasswordFormat("clear");
        }

        [Test]
        public void TestValidateUserWithWrongPasswordWithPasswordFormatHashed() {
            TestValidateUserWithWrongPasswordWithPasswordFormat("hashed");
        }

        [Test]
        public void TestValidateUserWithWrongPasswordWithPasswordFormatEncrypted() {
            TestValidateUserWithWrongPasswordWithPasswordFormat("encrypted");
        }

        /// <summary>
        /// Tests if the provider will return the correct username for a given email address when
        /// the email address is unique.
        /// </summary>
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

        /// <summary>
        /// Tests whether the provider will return null when looking for a non-existenet email address.
        /// </summary>
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

        /// <summary>
        /// Tests whether the provider will return the "lowest" username when the email address is
        /// not unique.
        /// </summary>
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

        /// <summary>
        /// Tests whether the provider will find users with an arbitrary Mongo query and sort.
        /// </summary>
        [Test]
        public void TestFindUsers() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);
            
            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test@test.com", "Test Question?", null, true, null, out status);    
            }

            int totalRecords;
            var users = provider.FindUsers(Query.Matches("UserName", new Regex(@"test1\d*")), SortBy.Ascending("UserName"), 0, 10, out totalRecords).ToArray();
            
            Assert.AreEqual(11, totalRecords);

            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(users[i].UserName.StartsWith("test1"));
            }
        }

        /// <summary>
        /// Tests whether the provider will throw an argument exception for an invalid skip.
        /// </summary>
        [Test]
        public void TestFindUsersWithInvalidSkip() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);
            
            Assert.Throws<ArgumentException>(() => {
                int totalRecords;
                provider.FindUsers(Query.EQ("UserName", "test"), SortBy.Ascending("UserName"), -1, 0, out totalRecords);
            });
        }

        /// <summary>
        /// Tests whether the provider will throw an argument exception for an invalid take.
        /// </summary>
        [Test]
        public void TestFindUsersWithInvalidTake() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            Assert.Throws<ArgumentException>(() => {
                int totalRecords;
                provider.FindUsers(Query.EQ("UserName", "test"), SortBy.Ascending("UserName"), 0, -1, out totalRecords);
            });
        }

        /// <summary>
        /// Tests whether the provider will retrieve all users with userNames that match a certain regex.
        /// </summary>
        [Test]
        public void TestFindUsersByName() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test@test.com", "Test Question?", null, true, null, out status);
            }

            int totalRecords;
            var users = provider.FindUsersByName(@"test1\d*", 0, 20, out totalRecords).Cast<MembershipUser>().ToArray();

            Assert.AreEqual(11, totalRecords);

            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(users[i].UserName.StartsWith("test1"));
            }
        }

        /// <summary>
        /// Tests whether the provider will retrieve all users with emails that match a certain regex.
        /// </summary>
        [Test]
        public void TestFindUsersByEmail() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test" + i + "@test.com", "Test Question?", null, true, null, out status);
            }

            int totalRecords;
            var users = provider.FindUsersByEmail(@"test1\d*@test.com", 0, 20, out totalRecords).Cast<MembershipUser>().ToArray();

            Assert.AreEqual(11, totalRecords);

            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(users[i].UserName.StartsWith("test1"));
            }
        }

    }

}
