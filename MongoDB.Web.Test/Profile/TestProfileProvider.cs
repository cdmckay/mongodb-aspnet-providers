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
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Profile;
using DigitalLiberationFront.MongoDB.Web.Security;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoDB.Web.Test.Profile {

    [TestFixture]
    public class TestProfileProvider {

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
            var server = MongoServer.Create("mongodb://localhost/aspnet");
            var database = server.GetDatabase("aspnet");
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

        #endregion

        #region Helpers

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

        private static void AddProviderSpecificPropertyValuesTo(SettingsPropertyValueCollection values, bool allowAnonymous) {
            var properties = new SettingsPropertyCollection();
            AddProviderSpecificPropertiesTo(properties, allowAnonymous);

            foreach (SettingsProperty p in properties) {
                values.Add(new SettingsPropertyValue(p) { PropertyValue = "Value of " + p.Name });
            }            
        }

        // String

        private static void AddStringPropertiesTo(SettingsPropertyCollection properties, bool allowAnonymous) {
            // DateTime chosen because there is a built-in DateTime TypeConverter.
            var dateTimeProperty = new SettingsProperty("dateTimeValue", typeof(DateTime), null,
                isReadOnly: false,
                defaultValue: null,
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

        #endregion

    }

}
