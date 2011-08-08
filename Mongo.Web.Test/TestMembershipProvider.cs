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

namespace DigitalLiberationFront.Mongo.Web.Security.Test {

    [TestFixture]
    public class TestMembershipProvider {

        private const string DefaultConnectionStringName = "MongoAspNetConString";
        private const string DefaultName = "MongoMembershipProvider";

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
            var membership = (MembershipSection) config.GetSection("system.web/membership");
            membership.DefaultProvider = DefaultName;
            var provider = new ProviderSettings(DefaultName, typeof(MongoMembershipProvider).AssemblyQualifiedName);
            provider.Parameters["connectionStringName"] = DefaultConnectionStringName;
            membership.Providers.Clear();
            membership.Providers.Add(provider);

            config.Save(ConfigurationSaveMode.Modified);
            ConfigurationManager.RefreshSection("connectionStrings");
            ConfigurationManager.RefreshSection("system.web/machineKey");
            ConfigurationManager.RefreshSection("system.web/membership");

            _config = new NameValueCollection {
                {"connectionStringName", DefaultConnectionStringName},
                {"minRequiredNonAlphanumericCharacters", "0"},
                {"requiresUniqueEmail", "false"}
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
        public void TestInitializeWithEnablePasswordRetrievalWithIrretrievablePassword() {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["passwordFormat"] = "hashed";

            var provider = new MongoMembershipProvider();
            Assert.Throws<ProviderException>(() => provider.Initialize(DefaultName, config));
        }

        #endregion

        #region CreateUser

        /// <summary>
        /// Tests whether a user is successfully created under normal circumstances.
        /// </summary>
        [Test]
        public void TestCreateUser() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", "Test question?", "Test answer.",
                                                  true, null, out status);

            Assert.NotNull(createdUser);
            Assert.AreEqual("test", createdUser.UserName);
            Assert.AreEqual("test@test.com", createdUser.Email);
            Assert.AreEqual("Test question?", createdUser.PasswordQuestion);
            Assert.IsTrue(createdUser.IsApproved);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
        }

        /// <summary>
        /// Tests whether a user is successfully created under normal circumstances.
        /// </summary>
        [Test]
        public void TestCreateUserWithRequireUniqueEmailWithDuplicateEmail() {
            var config = new NameValueCollection(_config);
            config["requiresUniqueEmail"] = "true";

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status1;
            provider.CreateUser("test1", "123456", "test@test.com", "Test question?", "Test answer.", true, null,
                                out status1);
            Assert.AreEqual(MembershipCreateStatus.Success, status1);

            MembershipCreateStatus status2;
            provider.CreateUser("test2", "123456", "test@test.com", "Test question?", "Test answer.", true, null,
                                out status2);
            Assert.AreEqual(MembershipCreateStatus.DuplicateEmail, status2);
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
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", "Test question?", "Test answer.",
                                                  true, providerUserKey, out status);

            Assert.NotNull(createdUser);
            Assert.AreEqual(providerUserKey, createdUser.ProviderUserKey);
            Assert.AreEqual("test", createdUser.UserName);
            Assert.AreEqual("test@test.com", createdUser.Email);
            Assert.AreEqual("Test question?", createdUser.PasswordQuestion);
            Assert.IsTrue(createdUser.IsApproved);
            Assert.AreEqual(MembershipCreateStatus.Success, status);
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

        #endregion

        #region GetUser

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

        #endregion

        #region ChangePassword

        private void TestChangePasswordWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);

            var changed = provider.ChangePassword("test", "123456", "654321");
            Assert.IsTrue(changed);

            var validated = provider.ValidateUser("test", "654321");
            Assert.IsTrue(validated);
        }

        [Test]
        public void TestChangePasswordWithPasswordFormatClear() {
            TestChangePasswordWithPasswordFormat("clear");
        }

        [Test]
        public void TestChangePasswordWithPasswordFormatHashed() {
            TestChangePasswordWithPasswordFormat("hashed");
        }

        [Test]
        public void TestChangePasswordWithPasswordFormatEncrypted() {
            TestChangePasswordWithPasswordFormat("encrypted");
        }

        private void TestChangePasswordWithWrongPasswordWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);

            var changed = provider.ChangePassword("test", "XXXXXX", "654321");
            Assert.IsFalse(changed);

            var validated = provider.ValidateUser("test", "654321");
            Assert.IsFalse(validated);
        }

        [Test]
        public void TestChangePasswordWithWrongPasswordWithPasswordFormatClear() {
            TestChangePasswordWithWrongPasswordWithPasswordFormat("clear");
        }

        [Test]
        public void TestChangePasswordWithWrongPasswordWithPasswordFormatHashed() {
            TestChangePasswordWithWrongPasswordWithPasswordFormat("hashed");
        }

        [Test]
        public void TestChangePasswordWithWrongPasswordWithPasswordFormatEncrypted() {
            TestChangePasswordWithWrongPasswordWithPasswordFormat("encrypted");
        }

        #endregion

        #region ChangePasswordQuestionAndAnswer

        private void TestChangePasswordQuestionAndAnswerWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "OldQuestion", "OldAnswer", true, null, out status);

            var changed = provider.ChangePasswordQuestionAndAnswer("test", "123456", "Question", "Answer");
            Assert.IsTrue(changed);

            var user = provider.GetUser("test", false);
            Assert.IsNotNull(user);
            Assert.AreEqual("Question", user.PasswordQuestion);
            // TODO Check the answer when ResetPassword implemented.
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

        private void TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "OldQuestion", "OldAnswer", true, null, out status);

            var changed = provider.ChangePasswordQuestionAndAnswer("test", "XXXXXX", "Question", "Answer");
            Assert.IsFalse(changed);

            var user = provider.GetUser("test", false);
            Assert.IsNotNull(user);
            Assert.AreEqual("OldQuestion", user.PasswordQuestion);
            // TODO Check answer when GetPassword implemented.
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormatClear() {
            TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormat("clear");
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormatHashed() {
            TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormat("hashed");
        }

        [Test]
        public void TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormatEncrypted() {
            TestChangePasswordQuestionAndAnswerWithWrongPasswordWithPasswordFormat("encrypted");
        }

        #endregion

        #region GetPassword

        private void TestGetPasswordWithoutPasswordRetrievalWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "false";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            Assert.Throws<ProviderException>(() => provider.GetPassword("test", "Answer"));
        }

        /// <summary>
        /// Test to make sure that GetPassword will fail when password retrieval is disabled and
        /// the password format is clear.
        /// </summary>
        [Test]
        public void TestGetPasswordWithoutPasswordRetrievalWithPasswordFormatClear() {
            TestGetPasswordWithoutPasswordRetrievalWithPasswordFormat("clear");
        }

        /// <summary>
        /// Test to make sure that GetPassword will fail when password retrieval is disabled and
        /// the password format is encrypted.
        /// </summary>
        [Test]
        public void TestGetPasswordWithoutPasswordRetrievalWithPasswordFormatEncrypted() {
            TestGetPasswordWithoutPasswordRetrievalWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            var password = provider.GetPassword("test", "Answer");
            Assert.AreEqual("123456", password);
        }

        /// <summary>
        /// Test to make sure that GetPassword works when password retrieval is enabled and a password question/answer
        /// is required and the password format is clear.
        /// </summary>
        [Test]
        public void TestGetPasswordWithPasswordRetrievalWithRequiresQAndAWithPasswordFormatClear() {
            TestGetPasswordWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat("clear");
        }

        /// <summary>
        /// Test to make sure that GetPassword works when password retrieval is enabled and a password question/answer
        /// is required and the password format is encrypted.
        /// </summary>
        [Test]
        public void TestGetPasswordWithPasswordRetrievalWithRequiresQAndAWithPasswordFormatEncrypted() {
            TestGetPasswordWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "false";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            var password = provider.GetPassword("test", "Wrong!");
            Assert.AreEqual("123456", password);
        }

        /// <summary>
        /// Test to make sure that GetPassword works when password retrieval is enabled and a password question/answer
        /// is NOT required and the password format is clear.
        /// </summary>
        [Test]
        public void TestGetPasswordWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormatClear() {
            TestGetPasswordWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat("clear");
        }

        /// <summary>
        /// Test to make sure that GetPassword works when password retrieval is enabled and a password question/answer
        /// is NOT required and the password format is encrypted.
        /// </summary>
        [Test]
        public void TestGetPasswordWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormatEncrypted() {
            TestGetPasswordWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            Assert.Throws<MembershipPasswordException>(() => provider.GetPassword("test", "Wrong!"));
        }

        [Test]
        public void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithRequiresQAndAWithPasswordFormatClear() {
            TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat("clear");
        }

        [Test]
        public void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithRequiresQAndAWithPasswordFormatEncrypted() {
            TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithRequiresQAndAWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "false";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            var password = provider.GetPassword("test", "Wrong!");
            Assert.AreEqual("123456", password);
        }

        [Test]
        public void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormatClear() {
            TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat("clear");
        }

        [Test]
        public void TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormatEncrypted() {
            TestGetPasswordWithWrongAnswerWithPasswordRetrievalWithoutRequiresQAndAWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithTooManyWrongAnswersWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["maxInvalidPasswordAttempts"] = "2";
            config["passwordFormat"] = passwordFormat;            

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            // This is used later to make sure the last lockout date was updated.
            var dateTimeAtStart = DateTime.Now;

            try {
                provider.GetPassword("test", "Wrong!");
            } catch (MembershipPasswordException) {                
            }

            try {
                provider.GetPassword("test", "Wrong!");
            } catch (MembershipPasswordException) {
            }

            var user = provider.GetUser("test", false);

            Assert.IsTrue(user.IsLockedOut);
            Assert.GreaterOrEqual(user.LastLockoutDate, dateTimeAtStart);
        }

        [Test]
        public void TestGetPasswordWithTooManyWrongAnswersWithPasswordFormatClear() {
            TestGetPasswordWithTooManyWrongAnswersWithPasswordFormat("clear");
        }

        [Test]
        public void TestGetPasswordWithTooManyWrongAnswersWithPasswordFormatEncrypted() {
            TestGetPasswordWithTooManyWrongAnswersWithPasswordFormat("encrypted");
        }

        private void TestGetPasswordWithTooFewWrongAnswersWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["maxInvalidPasswordAttempts"] = "2";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            try {
                provider.GetPassword("test", "Wrong!");
            } catch (MembershipPasswordException) {
            }
            
            var user = provider.GetUser("test", false);

            Assert.IsFalse(user.IsLockedOut);
        }

        [Test]
        public void TestGetPasswordWithTooFewWrongAnswersWithPasswordFormatClear() {
            TestGetPasswordWithTooFewWrongAnswersWithPasswordFormat("clear");
        }

        [Test]
        public void TestGetPasswordWithTooFewWrongAnswersWithPasswordFormatEncrypted() {
            TestGetPasswordWithTooFewWrongAnswersWithPasswordFormat("encrypted");
        }

        #endregion

        #region ResetPassword

        private void TestResetPasswordWithoutPasswordRetrievalWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordReset"] = "false";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            Assert.Throws<ProviderException>(() => provider.ResetPassword("test", "Answer"));
        }

        /// <summary>
        /// Test to make sure that ResetPassword will fail when password reset is disabled and
        /// the password format is clear.
        /// </summary>
        [Test]
        public void TestResetPasswordWithoutPasswordRetrievalWithPasswordFormatClear() {
            TestResetPasswordWithoutPasswordRetrievalWithPasswordFormat("clear");
        }

        /// <summary>
        /// Test to make sure that ResetPassword will fail when password reset is disabled and
        /// the password format is hashed.
        /// </summary>
        [Test]
        public void TestResetPasswordWithoutPasswordRetrievalWithPasswordFormatHashed() {
            TestResetPasswordWithoutPasswordRetrievalWithPasswordFormat("hashed");
        }

        /// <summary>
        /// Test to make sure that ResetPassword will fail when password reset is disabled and
        /// the password format is encrypted.
        /// </summary>
        [Test]
        public void TestResetPasswordWithoutPasswordRetrievalWithPasswordFormatEncrypted() {
            TestResetPasswordWithoutPasswordRetrievalWithPasswordFormat("encrypted");
        }

        private void TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordReset"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            provider.ResetPassword("test", "Answer");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormatClear() {
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormat("clear");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormatHashed() {
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormat("hashed");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormatEncrypted() {
            TestResetPasswordWithEnablePasswordResetWithRequiresQAndAWithPasswordFormat("encrypted");
        }

        private void TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormat(string passwordFormat) {
            var config = new NameValueCollection(_config);
            config["enablePasswordReset"] = "true";
            config["requiresQuestionAndAnswer"] = "false";
            config["passwordFormat"] = passwordFormat;

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            provider.ResetPassword("test", "Wrong!");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormatClear() {
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormat("clear");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormatHashed() {
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormat("hashed");
        }

        [Test]
        public void
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormatEncrypted() {
            TestResetPasswordWithEnablePasswordResetWithoutRequiresQAndAWithPasswordFormat("encrypted");
        }

        #endregion

        #region UpdateUser

        [Test]
        public void TestUpdateUser() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status;
            var createdUser = provider.CreateUser("test", "123456", "test@test.com", null, null, true, null, out status);
            createdUser.Email = "email@test.com";
            createdUser.Comment = "comment";
            createdUser.IsApproved = false;
            createdUser.LastLoginDate = new DateTime(1982, 04, 28);
            createdUser.LastActivityDate = new DateTime(1982, 04, 30);

            provider.UpdateUser(createdUser);
            var updatedUser = provider.GetUser(createdUser.ProviderUserKey, false);
            Assert.NotNull(updatedUser);
            Assert.AreEqual("email@test.com", updatedUser.Email);
            Assert.AreEqual("comment", updatedUser.Comment);
            Assert.AreEqual(false, updatedUser.IsApproved);
            Assert.AreEqual(new DateTime(1982, 04, 28), updatedUser.LastLoginDate);
            Assert.AreEqual(new DateTime(1982, 04, 30), updatedUser.LastActivityDate);
        }

        [Test]
        public void TestUpdateUserWithNullUser() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            Assert.Throws<ArgumentNullException>(() => provider.UpdateUser(null));
        }

        [Test]
        public void TestUpdateUserWithInvalidProviderUserKey() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            var user = new MembershipUser(
                providerName: provider.Name,
                name: "test",
                // This will cause the exception as it's not an ObjectId
                // or a parseable string version of an ObjectId.
                providerUserKey: new object(),
                email: "test@test.com",
                passwordQuestion: null,
                comment: null,
                isApproved: true,
                isLockedOut: false,
                creationDate: DateTime.Now,
                lastLoginDate: new DateTime(),
                lastActivityDate: new DateTime(),
                lastPasswordChangedDate: new DateTime(),
                lastLockoutDate: new DateTime());

            Assert.Throws<ProviderException>(() => provider.UpdateUser(user));
        }

        [Test]
        public void TestUpdateUserWithNonExistantProviderUserKey() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            var user = new MembershipUser(
                providerName: provider.Name,
                name: "test",
                // This will cause the exception as it's not an ObjectId
                // or a parseable string version of an ObjectId.
                providerUserKey: ObjectId.GenerateNewId(),
                email: "test@test.com",
                passwordQuestion: null,
                comment: null,
                isApproved: true,
                isLockedOut: false,
                creationDate: DateTime.Now,
                lastLoginDate: new DateTime(),
                lastActivityDate: new DateTime(),
                lastPasswordChangedDate: new DateTime(),
                lastLockoutDate: new DateTime());

            Assert.Throws<ProviderException>(() => provider.UpdateUser(user));
        }

        [Test]
        public void TestUpdateUserWithoutRequiresUniqueEmailWithDuplicateEmail() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status1;
            provider.CreateUser("test1", "123456", "test1@test.com", null, null, true, null, out status1);

            MembershipCreateStatus status2;
            var user = provider.CreateUser("test2", "123456", "test2@test.com", null, null, true, null, out status2);

            // Change the email to match the first user's email.
            user.Email = "test1@test.com";

            // No exception as duplicate emails are allowed.
            provider.UpdateUser(user);
        }

        [Test]
        public void TestUpdateUserWithRequiresUniqueEmailWithDuplicateEmail() {
            var config = new NameValueCollection(_config);
            config["requiresUniqueEmail"] = "true";

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status1;
            provider.CreateUser("test1", "123456", "test1@test.com", null, null, true, null, out status1);

            MembershipCreateStatus status2;
            var user = provider.CreateUser("test2", "123456", "test2@test.com", null, null, true, null, out status2);

            // Change the email to match the first user's email.
            user.Email = "test1@test.com";

            // Exception as duplicate emails are NOT allowed.
            Assert.Throws<ProviderException>(() => provider.UpdateUser(user));
        }

        [Test]
        public void TestUpdateUserWithDuplicateUserName() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            MembershipCreateStatus status1;
            provider.CreateUser("test1", "123456", "test2@test.com", null, null, true, null, out status1);

            MembershipCreateStatus status2;
            var createdUser = provider.CreateUser("test2", "123456", "test2@test.com", null, null, true, null, out status2);

            // Since we can't change the UserName property directly, we create a MembershipUser with the same values
            Assert.IsNotNull(createdUser);
            Assert.IsNotNull(createdUser.ProviderUserKey);
            var duplicateUser = new MembershipUser(
                providerName: createdUser.ProviderName,
                // Change the name to match the first user.
                name: "test1",
                providerUserKey: createdUser.ProviderUserKey,
                email: createdUser.Email,
                passwordQuestion: createdUser.PasswordQuestion,
                comment: createdUser.Comment,
                isApproved: createdUser.IsApproved,
                isLockedOut: createdUser.IsLockedOut,
                creationDate: createdUser.CreationDate,
                lastLoginDate: createdUser.LastLoginDate,
                lastActivityDate: createdUser.LastActivityDate,
                lastPasswordChangedDate: createdUser.LastPasswordChangedDate,
                lastLockoutDate: createdUser.LastLockoutDate);

            // Exception as duplicate usernames are NOT allowed.
            Assert.Throws<ProviderException>(() => provider.UpdateUser(duplicateUser));
        }

        #endregion

        #region UnlockUser

        [Test]
        public void TestUnlockUser() {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["maxInvalidPasswordAttempts"] = "1";
            config["passwordFormat"] = "clear";

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            try {
                provider.GetPassword("test", "Wrong!");    
            } catch (MembershipPasswordException) {                
            }                      

            // User will now be locked.
            var lockedOutUser = provider.GetUser("test", false);
            Assert.IsTrue(lockedOutUser.IsLockedOut);

            var unlocked = provider.UnlockUser("test");
            Assert.IsTrue(unlocked);

            // User will now be unlocked.
            var user = provider.GetUser("test", false);
            Assert.IsFalse(user.IsLockedOut);
        }

        [Test]
        public void TestUnlockUserWithNonExistentUser() {
            var config = new NameValueCollection(_config);
            config["enablePasswordRetrieval"] = "true";
            config["requiresQuestionAndAnswer"] = "true";
            config["maxInvalidPasswordAttempts"] = "1";
            config["passwordFormat"] = "clear";

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            var unlocked = provider.UnlockUser("Wrong!");
            Assert.IsFalse(unlocked);
        }

        #endregion

        #region DeleteUser

        [Test]
        public void TestDeleteUser() {
            var config = new NameValueCollection(_config);

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            MembershipCreateStatus status;
            provider.CreateUser("test", "123456", "test@test.com", "Question", "Answer", true, null, out status);

            var deleted = provider.DeleteUser("test", true);
            Assert.IsTrue(deleted);

            var deletedUser = provider.GetUser("test", false);
            Assert.IsNull(deletedUser);
        }

        [Test]
        public void TestDeleteUserWithNonExistentUser() {
            var config = new NameValueCollection(_config);

            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, config);

            var deleted = provider.DeleteUser("test", true);
            Assert.IsFalse(deleted);
        }

        #endregion

        #region ValidateUser

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

        #endregion

        #region GetUserNameByEmail

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

        #endregion

        #region FindUsers

        /// <summary>
        /// Tests whether the provider will find users with an arbitrary Mongo query and sort.
        /// </summary>
        [Test]
        public void TestFindUsers() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test@test.com", "Test Question?", null, true, null,
                                    out status);
            }

            int totalRecords;
            var users =
                provider.FindUsers(Query.Matches("UserName", new Regex(@"test1\d*")), SortBy.Ascending("UserName"), 0,
                                   10, out totalRecords).ToArray();

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

        #endregion

        #region FindUsersByName

        /// <summary>
        /// Tests whether the provider will retrieve all users with userNames that match a certain regex.
        /// </summary>
        [Test]
        public void TestFindUsersByName() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test@test.com", "Test Question?", null, true, null,
                                    out status);
            }

            int totalRecords;
            var users = provider.FindUsersByName(@"test1\d*", 0, 20, out totalRecords).Cast<MembershipUser>().ToArray();

            Assert.AreEqual(11, totalRecords);

            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(users[i].UserName.StartsWith("test1"));
            }
        }

        #endregion

        #region FindUsersByEmail

        /// <summary>
        /// Tests whether the provider will retrieve all users with emails that match a certain regex.
        /// </summary>
        [Test]
        public void TestFindUsersByEmail() {
            var provider = new MongoMembershipProvider();
            provider.Initialize(DefaultName, _config);

            for (int i = 0; i < 100; i++) {
                MembershipCreateStatus status;
                provider.CreateUser("test" + i, "123456", "test" + i + "@test.com", "Test Question?", null, true, null,
                                    out status);
            }

            int totalRecords;
            var users =
                provider.FindUsersByEmail(@"test1\d*@test.com", 0, 20, out totalRecords).Cast<MembershipUser>().ToArray();

            Assert.AreEqual(11, totalRecords);

            for (int i = 0; i < 10; i++) {
                Assert.IsTrue(users[i].UserName.StartsWith("test1"));
            }
        }

        #endregion

    }

}
