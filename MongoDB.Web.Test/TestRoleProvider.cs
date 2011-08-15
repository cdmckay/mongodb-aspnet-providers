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
using System.Configuration.Provider;
using System.Web.Security;
using MongoDB.Driver;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoDB.Web.Security.Test {

    [TestFixture]
    public class TestRoleProvider {

        private const string DefaultMembershipName = TestHelper.DefaultMembershipName;
        private const string DefaultRoleName = TestHelper.DefaultRoleName;

        private NameValueCollection _membershipConfig;
        private NameValueCollection _roleConfig;

        #region Test SetUp and TearDown

        [TestFixtureSetUp]
        public void TestFixtureSetUp() {
            TestHelper.ConfigureConnectionStrings();
            _membershipConfig = TestHelper.ConfigureMembershipProvider(DefaultMembershipName);
            _roleConfig = TestHelper.ConfigureRoleProvider(DefaultRoleName);
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
            var config = new NameValueCollection(_roleConfig);

            var provider = new MongoRoleProvider();
            Assert.Throws<InvalidOperationException>(() => {
                provider.Initialize(DefaultRoleName, config);
                provider.Initialize(DefaultRoleName, config);
            });
        }      

        #endregion    

        #region IsUserInRole

        [Test]
        public void TestIsUserInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            roleProvider.CreateRole("role1");
            roleProvider.AddUsersToRoles(new[] { "user1", }, new[] { "role1" });

            Assert.IsTrue(roleProvider.IsUserInRole("user1", "role1"));
        }

        [Test]
        public void TestIsUserInRoleWhenNotInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");
            roleProvider.AddUsersToRoles(new[] { "user1", }, new[] { "role1" });

            Assert.IsFalse(roleProvider.IsUserInRole("user1", "role2"));
        }

        [Test]
        public void TestIsUserInRoleWhenUserDoesNotExist() {
            var roleConfig = new NameValueCollection(_roleConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);
            roleProvider.CreateRole("role1");

            Assert.Throws<ArgumentException>(() => roleProvider.IsUserInRole("user1", "role1"));
        }

        [Test]
        public void TestIsUserInRoleWhenRoleDoesNotExist() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);

            Assert.Throws<ArgumentException>(() => roleProvider.IsUserInRole("user1", "role1"));
        }

        #endregion

        #region CreateRole

        [Test]
        public void TestCreateRole() {
            var config = new NameValueCollection(_roleConfig);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultRoleName, config);
            
            provider.CreateRole("test");
        }

        [Test]
        public void TestCreateRoleWithDuplicateRoleName() {
            var config = new NameValueCollection(_roleConfig);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultRoleName, config);

            provider.CreateRole("test");
            Assert.Throws<ProviderException>(() => provider.CreateRole("test"));
        }

        [Test]
        public void TestCreateRoleWithCommaRoleName() {
            var config = new NameValueCollection(_roleConfig);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultRoleName, config);

            Assert.Throws<ArgumentException>(() => provider.CreateRole("test,"));
        }

        #endregion

        #region DeleteRole

        [Test]
        public void TestDeleteRole() {
            var roleConfig = new NameValueCollection(_roleConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            roleProvider.CreateRole("role1");
            Assert.IsTrue(roleProvider.RoleExists("role1"));
            roleProvider.DeleteRole("role1", true);
            Assert.IsFalse(roleProvider.RoleExists("role1"));
        }

        [Test]
        public void TestDeleteRoleWhenPopulatedWithNoThrowOnPopulated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" });

            roleProvider.DeleteRole("role1", false);
            Assert.IsFalse(roleProvider.RoleExists("role1"));
            Assert.Throws<ArgumentException>(() => roleProvider.IsUserInRole("user1", "role1"));
        }

        [Test]
        public void TestDeleteRoleWhenPopulatedWithThrowOnPopulated() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" });

                        
            Assert.Throws<ProviderException>(() => roleProvider.DeleteRole("role1", true));
        }

        #endregion

        #region RoleExists

        [Test]
        public void TestRoleExists() {
            var config = new NameValueCollection(_roleConfig);

            var provider = new MongoRoleProvider();
            provider.Initialize(DefaultRoleName, config);

            Assert.IsFalse(provider.RoleExists("test"));
            provider.CreateRole("test");
            Assert.IsTrue(provider.RoleExists("test"));
        }

        #endregion

        #region AddUsersToRoles

        [Test]
        public void TestAddUsersToRoles() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" });
            Assert.IsTrue(roleProvider.IsUserInRole("user1", "role1"));
            Assert.IsTrue(roleProvider.IsUserInRole("user1", "role2"));
            Assert.IsTrue(roleProvider.IsUserInRole("user2", "role1"));
            Assert.IsTrue(roleProvider.IsUserInRole("user2", "role2"));
        }

        [Test]
        public void TestAddUsersToRolesWhenAlreadyInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");

            roleProvider.AddUsersToRoles(new[] { "user1", }, new[] { "role1" });
            roleProvider.AddUsersToRoles(new[] { "user2", }, new[] { "role2" });

            Assert.Throws<ProviderException>(
                () => roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" }));
        }

        [Test]
        public void TestAddUsersToRolesWithNonExistantUserNames() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            Assert.Throws<ProviderException>(
                () => roleProvider.AddUsersToRoles(new[] {"user1", "user2"}, new[] {"role1", "role2"}));
        }

        [Test]
        public void TestAddUsersToRolesWithNonExistantRoles() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            Assert.Throws<ProviderException>(
                () => roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" }));
        }

        #endregion

        #region RemoveUsersFromRoles

        [Test]
        public void TestRemoveUsersFromRoles() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");
            roleProvider.CreateRole("role3");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2", "role3" });
            roleProvider.RemoveUsersFromRoles(new[] { "user1", "user2" }, new[] { "role1", "role3" });
            Assert.IsFalse(roleProvider.IsUserInRole("user1", "role1"));
            Assert.IsFalse(roleProvider.IsUserInRole("user2", "role1"));
            Assert.IsTrue(roleProvider.IsUserInRole("user2", "role2"));
            Assert.IsTrue(roleProvider.IsUserInRole("user2", "role2"));
            Assert.IsFalse(roleProvider.IsUserInRole("user1", "role3"));
            Assert.IsFalse(roleProvider.IsUserInRole("user2", "role3"));
        }

        [Test]
        public void TestRemoveUsersFromRolesWhenNotInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");
            roleProvider.CreateRole("role3");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" });
            Assert.Throws<ProviderException>(
                () => roleProvider.RemoveUsersFromRoles(new[] {"user1", "user2"}, new[] {"role1", "role3"}));
        }

        [Test]
        public void TestRemoveUsersFromRolesWithNonExistantUserNames() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");

            Assert.Throws<ProviderException>(
                () => roleProvider.RemoveUsersFromRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" }));
        }

        [Test]
        public void TestRemoveUsersFromRolesWithNonExistantRoles() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);

            Assert.Throws<ProviderException>(
                () => roleProvider.RemoveUsersFromRoles(new[] { "user1", "user2" }, new[] { "role1", "role2" }));
        }

        #endregion

        #region GetUsersInRole

        [Test]
        public void TestGetUsersInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user3", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");

            roleProvider.AddUsersToRoles(new[] { "user1", "user2" }, new[] { "role1" });

            var userNames = roleProvider.GetUsersInRole("role1");
            Assert.AreEqual(2, userNames.Length);
            Assert.Contains("user1", userNames);
            Assert.Contains("user2", userNames);
        }

        [Test]
        public void TestGetUsersInRoleWhenNoUsersInRole() {
            var membershipConfig = new NameValueCollection(_membershipConfig);
            var roleConfig = new NameValueCollection(_roleConfig);

            var membershipProvider = new MongoMembershipProvider();
            membershipProvider.Initialize(DefaultMembershipName, membershipConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            MembershipCreateStatus status;
            membershipProvider.CreateUser("user1", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user2", "123456", "test@test.com", null, null, true, null, out status);
            membershipProvider.CreateUser("user3", "123456", "test@test.com", null, null, true, null, out status);

            roleProvider.CreateRole("role1");

            var userNames = roleProvider.GetUsersInRole("role1");
            Assert.AreEqual(0, userNames.Length);      
        }

        #endregion

        #region GetAllRoles

        [Test]
        public void TestGetAllRoles() {
            var roleConfig = new NameValueCollection(_roleConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);
            
            roleProvider.CreateRole("role1");
            roleProvider.CreateRole("role2");
            roleProvider.CreateRole("role3");

            var roles = roleProvider.GetAllRoles();
            Assert.AreEqual(3, roles.Length);
            Assert.Contains("role1", roles);
            Assert.Contains("role2", roles);
            Assert.Contains("role3", roles);
        }

        [Test]
        public void TestGetAllRolesWhenNoRoles() {
            var roleConfig = new NameValueCollection(_roleConfig);

            var roleProvider = new MongoRoleProvider();
            roleProvider.Initialize(DefaultRoleName, roleConfig);

            var roles = roleProvider.GetAllRoles();
            Assert.AreEqual(0, roles.Length);
        }

        #endregion

    }

}
