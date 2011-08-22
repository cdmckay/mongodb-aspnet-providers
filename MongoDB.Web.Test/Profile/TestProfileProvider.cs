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
            AddSimpleSettingsPropertyValuesTo(collection, allowAnonymous: false);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext("user", true), collection);
        }

        [Test]
        public void TestSetPropertyValuesWithAnonymousUser() {
            var profileConfig = new NameValueCollection(_profileConfig);
            var profileProvider = new MongoProfileProvider();
            profileProvider.Initialize(DefaultProfileName, profileConfig);

            var collection = new SettingsPropertyValueCollection();
            AddSimpleSettingsPropertyValuesTo(collection, allowAnonymous: true);
            profileProvider.SetPropertyValues(TestHelper.GenerateSettingsContext(ObjectId.GenerateNewId().ToString(), false), collection);
        }

        #endregion

        #region Helpers

        private static void AddSimpleSettingsPropertyValuesTo(SettingsPropertyValueCollection collection, bool allowAnonymous) {
            var firstNameProperty = new SettingsProperty("firstName", typeof(string), null,
                isReadOnly: false,
                defaultValue: null,
                serializeAs: SettingsSerializeAs.ProviderSpecific,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);

            var lastNameProperty = new SettingsProperty("lastName", typeof(string), null,
                isReadOnly: false,
                defaultValue: null,
                serializeAs: SettingsSerializeAs.ProviderSpecific,
                attributes: new SettingsAttributeDictionary { { "AllowAnonymous", allowAnonymous } },
                throwOnErrorDeserializing: false,
                throwOnErrorSerializing: false);

            collection.Add(new SettingsPropertyValue(firstNameProperty) { PropertyValue = "John" });
            collection.Add(new SettingsPropertyValue(lastNameProperty) { PropertyValue = "Doe" });
        }

        #endregion

    }

}
