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
using System.Web.Profile;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Profile;
using DigitalLiberationFront.MongoDB.Web.Security;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoDB.Web.Test.Profile {

    [TestFixture]
    public class TestProfileProvider {

        private const string ConnectionString = "mongodb://localhost/aspnet";
        private const string DefaultMembershipName = TestHelper.DefaultMembershipName;
        private const string DefaultProfileName = TestHelper.DefaultProfileName;

        private NameValueCollection _membershipConfig;
        private NameValueCollection _profileConfig;        

        #region Test SetUp and TearDown

        [TestFixtureSetUp]
        public void TestFixtureSetUp() {
            TestHelper.ConfigureConnectionStrings();
            _membershipConfig = TestHelper.ConfigureMembershipProvider(DefaultMembershipName);
            _profileConfig = TestHelper.ConfigureRoleProvider(DefaultProfileName);
        }

        [TestFixtureTearDown]
        public void TestFixtureTearDown() {

        }

        [SetUp]
        public void SetUp() {
            var url = new MongoUrl(ConnectionString);
            var server = MongoServer.Create(url);
            var database = server.GetDatabase(url.DatabaseName);
            database.Drop();
        }

        #endregion

        #region Initialize

        [Test]
        public void TestInitializeWhenCalledTwice() {
            var config = new NameValueCollection(_profileConfig);
            var provider = new MongoProfileProvider();
            Assert.Throws<InvalidOperationException>(() => {
                provider.Initialize(DefaultProfileName, config);
                provider.Initialize(DefaultProfileName, config);
            });
        }      

        #endregion    

        #region SetPropertyValues

        [Test]
        public void TestSetPropertyValues() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user", "123456", "test@test.com", null, null, true, null, out status);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var collection = new SettingsPropertyValueCollection();
            AddProviderSpecificPropertyValuesTo(collection, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), collection);
        }

        [Test]
        public void TestSetPropertyValuesWithAnonymousUser() {
            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var userName = Guid.NewGuid().ToString();
            var collection = new SettingsPropertyValueCollection();
            AddProviderSpecificPropertyValuesTo(collection, allowAnonymous: true);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext(userName, false), collection);
        }

        #endregion

        #region GetPropertyValues

        [Test]
        public void TestGetPropertyValuesUsingProviderSpecificProperties() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user", "123456", "test@test.com", null, null, true, null, out status);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var values = new SettingsPropertyValueCollection();
            AddProviderSpecificPropertyValuesTo(values, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), values);

            var properties = new SettingsPropertyCollection();
            AddProviderSpecificPropertiesTo(properties, allowAnonymous: false);

            var retrievedValues = profileProvider
                .GetPropertyValues(TestHelper.GenerateSettingsContext("user", true), properties);
            var rawRetrievedValues = retrievedValues
                .Cast<SettingsPropertyValue>()
                .Select(value => value.PropertyValue)
                .ToList();
            Assert.AreEqual(2, retrievedValues.Count);
            Assert.Contains("Value of stringValue1", rawRetrievedValues);
            Assert.Contains("Value of stringValue2", rawRetrievedValues);
        }

        [Test]
        public void TestGetPropertyValuesUsingProviderSpecificPropertiesWithAnonymousUser() {
            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var userName = Guid.NewGuid().ToString();
            var values = new SettingsPropertyValueCollection();
            AddProviderSpecificPropertyValuesTo(values, allowAnonymous: true);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext(userName, false), values);

            var properties = new SettingsPropertyCollection();
            AddProviderSpecificPropertiesTo(properties, allowAnonymous: true);

            var retrievedValues = profileProvider
                .GetPropertyValues(TestHelper.GenerateSettingsContext(userName, false), properties);
            var rawRetrievedValues = retrievedValues
                .Cast<SettingsPropertyValue>()
                .Select(value => value.PropertyValue)
                .ToList();
            Assert.AreEqual(2, retrievedValues.Count);
            Assert.Contains("Value of stringValue1", rawRetrievedValues);
            Assert.Contains("Value of stringValue2", rawRetrievedValues);
        }

        [Test]
        public void TestGetPropertyValuesUsingStringProperties() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user", "123456", "test@test.com", null, null, true, null, out status);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var values = new SettingsPropertyValueCollection();
            AddStringPropertyValuesTo(values, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), values);

            var properties = new SettingsPropertyCollection();
            AddStringPropertiesTo(properties, allowAnonymous: false);

            var retrievedValues = profileProvider
                .GetPropertyValues(TestHelper.GenerateSettingsContext("user", true), properties);
            var rawRetrievedValues = retrievedValues
                .Cast<SettingsPropertyValue>()
                .Select(value => value.PropertyValue)
                .ToList();
            Assert.AreEqual(1, retrievedValues.Count);
            Assert.Contains(new DateTime(1982, 4, 28), rawRetrievedValues);
        }        

        [Test]
        public void TestGetPropertyValuesUsingXmlProperties() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user", "123456", "test@test.com", null, null, true, null, out status);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var values = new SettingsPropertyValueCollection();
            AddXmlPropertyValuesTo(values, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), values);

            var properties = new SettingsPropertyCollection();
            AddXmlPropertiesTo(properties, allowAnonymous: false);

            var retrievedValues = profileProvider
                .GetPropertyValues(TestHelper.GenerateSettingsContext("user", true), properties);
            var rawRetrievedValues = retrievedValues
                .Cast<SettingsPropertyValue>()
                .Select(value => value.PropertyValue)
                .ToList();
            Assert.AreEqual(1, retrievedValues.Count);
            Assert.Contains("Value of stringValue", rawRetrievedValues);            
        }

        [Test]
        public void TestGetPropertyValuesUsingBinaryProperties() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user", "123456", "test@test.com", null, null, true, null, out status);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var values = new SettingsPropertyValueCollection();
            AddBinaryPropertyValuesTo(values, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), values);

            var properties = new SettingsPropertyCollection();
            AddBinaryPropertiesTo(properties, allowAnonymous: false);

            var retrievedValues = profileProvider
                .GetPropertyValues(TestHelper.GenerateSettingsContext("user", true), properties);
            var rawRetrievedValues = retrievedValues
                .Cast<SettingsPropertyValue>()
                .Select(value => value.PropertyValue)
                .ToList();
            Assert.AreEqual(1, retrievedValues.Count);
            Assert.Contains(new List<string> { "foo", "bar" }, rawRetrievedValues);
        }     

        #endregion

        #region GetAllProfiles        

        [Test]
        public void TestGetAllProfiles() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);
            
            int totalRecords = 0;
            var profiles = profileProvider.GetAllProfiles(ProfileAuthenticationOption.All, 0, 30, out totalRecords);

            Assert.AreEqual(80, totalRecords);
            Assert.AreEqual(30, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.AreEqual("user", p.UserName.Substring(0, 4));
                Assert.Greater(p.Size, 0);
            }
        }

        [Test]
        public void TestGetAllProfilesThatAreAuthenticated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.GetAllProfiles(ProfileAuthenticationOption.Authenticated, 0, 30, out totalRecords);

            Assert.AreEqual(40, totalRecords);
            Assert.AreEqual(30, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.AreEqual("user", p.UserName.Substring(0, 4));

                // All even records are authenticated in this test.
                Assert.IsTrue(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsFalse(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        [Test]
        public void TestGetAllProfilesThatAreAnonymous() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.GetAllProfiles(ProfileAuthenticationOption.Anonymous, 0, 30, out totalRecords);

            Assert.AreEqual(40, totalRecords);
            Assert.AreEqual(30, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.AreEqual("user", p.UserName.Substring(0, 4));

                // All even records are authenticated in this test.
                Assert.IsFalse(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsTrue(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        #endregion

        #region GetAllInactiveProfiles                

        [Test]
        public void TestGetAllInActiveProfilesThatAreAuthenticated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.GetAllInactiveProfiles(ProfileAuthenticationOption.Authenticated, DateTime.Now.AddDays(-1), 0, 30, out totalRecords);

            Assert.AreEqual(20, totalRecords);
            Assert.AreEqual(20, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.AreEqual("user", p.UserName.Substring(0, 4));

                // All even records are authenticated in this test.
                Assert.IsTrue(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsFalse(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        [Test]
        public void TestGetAllInactiveProfilesThatAreAnonymous() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.GetAllInactiveProfiles(ProfileAuthenticationOption.Anonymous, DateTime.Now.AddDays(-1), 0, 30, out totalRecords);

            Assert.AreEqual(20, totalRecords);
            Assert.AreEqual(20, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.AreEqual("user", p.UserName.Substring(0, 4));

                // All even records are authenticated in this test.
                Assert.IsFalse(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsTrue(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        #endregion

        #region FindProfilesByUserName

        [Test]
        public void TestFindProfilesByUserNameThatAreAuthenticated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.FindProfilesByUserName(ProfileAuthenticationOption.Authenticated, @"user2\d*", 0, 2, out totalRecords);

            Assert.AreEqual(5, totalRecords);
            Assert.AreEqual(2, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.IsTrue(p.UserName.StartsWith("user2"));

                // All even records are authenticated in this test.
                Assert.IsTrue(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsFalse(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        [Test]
        public void TestFindProfilesByUserNameThatAreAnonymous() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.FindProfilesByUserName(ProfileAuthenticationOption.Anonymous, @"user2\d*", 0, 2, out totalRecords);

            Assert.AreEqual(5, totalRecords);
            Assert.AreEqual(2, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.IsTrue(p.UserName.StartsWith("user2"));

                // All even records are authenticated in this test.
                Assert.IsFalse(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsTrue(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        #endregion

        #region FindInactiveProfilesByUserName

        [Test]
        public void TestFindInactiveProfilesByUserNameThatAreAuthenticated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.FindInactiveProfilesByUserName(
                ProfileAuthenticationOption.Authenticated, @"user\d*(0|1)", DateTime.Now.AddDays(-1), 0, 2, out totalRecords);

            Assert.AreEqual(4, totalRecords);
            Assert.AreEqual(2, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.IsTrue(p.UserName.StartsWith("user") && (p.UserName.EndsWith("0") || p.UserName.EndsWith("1")));

                // All even records are authenticated in this test.
                Assert.IsTrue(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsFalse(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        [Test]
        public void TestFindInactiveProfilesByUserNameThatAreAnonymous() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            SetUpTestProfiles(membershipProvider, profileProvider);

            int totalRecords = 0;
            var profiles = profileProvider.FindInactiveProfilesByUserName(
                ProfileAuthenticationOption.Anonymous, @"user\d*(0|1)", DateTime.Now.AddDays(-1), 0, 2, out totalRecords);

            Assert.AreEqual(4, totalRecords);
            Assert.AreEqual(2, profiles.Count);
            foreach (ProfileInfo p in profiles) {
                Assert.IsTrue(p.UserName.StartsWith("user") && (p.UserName.EndsWith("0") || p.UserName.EndsWith("1")));

                // All even records are authenticated in this test.
                Assert.IsFalse(Convert.ToInt32(p.UserName.Substring(4)) % 2 == 0);

                Assert.IsTrue(p.IsAnonymous);
                Assert.Greater(p.Size, 0);
            }
        }

        #endregion

        #region Helpers

        private void SetUpTestProfiles(MongoMembershipProvider membershipProvider, MongoProfileProvider profileProvider) {
            // Make 20 users that have no profiles.            
            for (int i = 0; i < 20; i++) {
                MembershipCreateStatus status;
                membershipProvider.CreateUser("user" + i, "123456", "user" + i + "@test.com", null, null, true, null, out status);
            }

            // Make 80 users that have profiles, half of them anonymous.
            for (int i = 20; i < 100; i++) {
                bool isAuthenticated = i % 2 == 0;

                if (isAuthenticated) {
                    MembershipCreateStatus status;
                    membershipProvider.CreateUser("user" + i, "123456", "user" + i + "@test.com", null, null, true, null, out status);
                }

                var values = new SettingsPropertyValueCollection();
                AddProviderSpecificPropertyValuesTo(values, allowAnonymous: true, prefix: string.Format("({0})", i));
                profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user" + i, isAuthenticated), values);
            }

            // Get a direction connection to the database so we can edit the LastActivityDate.
            var url = new MongoUrl(ConnectionString);
            var server = MongoServer.Create(url);
            var database = server.GetDatabase(url.DatabaseName);
            var collection = database.GetCollection(membershipProvider.ApplicationName + ".users");

            var inactiveDate = DateTime.Now.AddDays(-10);

            // Make half of all profiled users inactive.            
            var query = Query.Where("this.UserName.substr(4) >= 60");
            var update = Update.Set("Profile.LastActivityDate", SerializationHelper.SerializeDateTime(inactiveDate));
            collection.Update(query, update, UpdateFlags.Multi);
        }

        // Provider Specific

        private static void AddProviderSpecificPropertiesTo(SettingsPropertyCollection properties, bool allowAnonymous) {
            var stringProperty1 = new SettingsProperty("stringValue1", typeof(string), null,
                isReadOnly: false,
                defaultValue: null,
                serializeAs: SettingsSerializeAs.ProviderSpecific,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);
            properties.Add(stringProperty1);

            var stringProperty2 = new SettingsProperty("stringValue2", typeof(string), null,
                isReadOnly: false,
                defaultValue: null,
                serializeAs: SettingsSerializeAs.ProviderSpecific,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);
            properties.Add(stringProperty2);
        }

        private static void AddProviderSpecificPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous, string prefix) {
            var properties = new SettingsPropertyCollection();
            AddProviderSpecificPropertiesTo(properties, allowAnonymous);

            foreach (SettingsProperty p in properties) {
                values.Add(new SettingsPropertyValue(p) { PropertyValue = prefix + "Value of " + p.Name });
            }            
        }

        private static void AddProviderSpecificPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous) {
            AddProviderSpecificPropertyValuesTo(values, allowAnonymous, string.Empty);
        }

        // String

        private static void AddStringPropertiesTo(SettingsPropertyCollection properties, bool allowAnonymous) {
            // DateTime chosen because there is a built-in DateTime TypeConverter.
            var dateTimeProperty = new SettingsProperty("dateTimeValue", typeof(DateTime), null,
                isReadOnly: false,
                defaultValue: new DateTime(),
                serializeAs: SettingsSerializeAs.String,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);
            properties.Add(dateTimeProperty);
        }

        private static void AddStringPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous) {
            var properties = new SettingsPropertyCollection();
            AddStringPropertiesTo(properties, allowAnonymous);

            values.Add(new SettingsPropertyValue(properties["dateTimeValue"]) { PropertyValue = new DateTime(1982, 4, 28) });              
        }

        // XML

        private static void AddXmlPropertiesTo(SettingsPropertyCollection properties, bool allowAnonymous) {
            var xmlStringProperty = new SettingsProperty("stringValue", typeof(string), null,
                isReadOnly: false,
                defaultValue: null,
                serializeAs: SettingsSerializeAs.Xml,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);
            properties.Add(xmlStringProperty);
        }

        private static void AddXmlPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous) {
            var properties = new SettingsPropertyCollection();
            AddXmlPropertiesTo(properties, allowAnonymous);

            var xmlStringProperty = properties["stringValue"];
            values.Add(new SettingsPropertyValue(xmlStringProperty) { PropertyValue = "Value of " + xmlStringProperty.Name });
        }

        // Binary

        private static void AddBinaryPropertiesTo(SettingsPropertyCollection properties, bool allowAnonymous) {
            var listOfStringsProperty = new SettingsProperty("listOfStrings", typeof(List<string>), null,
                isReadOnly: false,
                defaultValue: new List<string>(),
                serializeAs: SettingsSerializeAs.Binary,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);
            properties.Add(listOfStringsProperty);
        }

        private static void AddBinaryPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous) {
            var properties = new SettingsPropertyCollection();
            AddBinaryPropertiesTo(properties, allowAnonymous);

            var listOfStringsProperty = properties["listOfStrings"];
            values.Add(new SettingsPropertyValue(listOfStringsProperty) { PropertyValue = new List<string> { "foo", "bar" } });
        }

        #endregion

    }

}
