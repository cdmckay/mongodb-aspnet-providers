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
using System.IO;
using System.Threading;
using System.Web;
using System.Web.SessionState;
using DigitalLiberationFront.MongoDB.Web.SessionState;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace DigitalLiberationFront.MongoDB.Web.Test.SessionState {

    [TestFixture]
    public class TestSessionStateStore {

        private const string ConnectionString = "mongodb://localhost/aspnet";
        private const string DefaultSessionName = TestHelper.DefaultSessionName;

        const int DefaultTimeout = 20;

        private NameValueCollection _sessionConfig;

        #region Test SetUp and TearDown

        [TestFixtureSetUp]
        public void TestFixtureSetUp() {
            TestHelper.ConfigureConnectionStrings();
            _sessionConfig = TestHelper.ConfigureSessionProvider(DefaultSessionName);
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
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            Assert.Throws<InvalidOperationException>(() => {
                provider.Initialize(DefaultSessionName, config);
                provider.Initialize(DefaultSessionName, config);
            });
        }  
    
        #endregion

        #region CreateUninitializedItem

        [Test]
        public void TestCreateUninitializedItem() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            provider.CreateUninitializedItem(CreateHttpContext(), GenerateSessionId(), DefaultTimeout);
        }

        #endregion

        #region GetItem

        [Test]
        public void TestGetItem() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;
            var storeData = provider.GetItem(context, sessionId, out locked, out lockAge, out lockId, out actions);

            Assert.IsNotNull(storeData);
            Assert.IsFalse(locked);
            Assert.AreNotEqual(ObjectId.Empty, lockId);

            // Actions flag should be InitializeItem since it was created uninitialized above.
            Assert.AreEqual(SessionStateActions.InitializeItem, actions);
        }

        [Test]
        public void TestGetItemWhenDoesNotExist() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;
            var storeData = provider.GetItem(context, sessionId, out locked, out lockAge, out lockId, out actions);

            Assert.IsNull(storeData);
            Assert.IsFalse(locked);
        }

        [Test]
        public void TestGetItemWhenExpired() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, 0);

            // Give it a chance to expire.
            Thread.Sleep(100);

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;
            var storeData = provider.GetItem(context, sessionId, out locked, out lockAge, out lockId, out actions);

            Assert.IsNull(storeData);
            Assert.IsFalse(locked);
        }

        [Test]
        public void TestGetItemWhenLocked() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            // Call exclusive version of get item to force a lock.
            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            provider.GetItemExclusive(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);

            // Sleep for a second to ensure the lockAge can be greater than 0 if it exists.
            Thread.Sleep(100);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var storeData = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);

            Assert.IsNull(storeData);
            Assert.IsTrue(locked2);
            Assert.Greater(lockAge2, TimeSpan.Zero);
            Assert.AreEqual(lockId1, lockId2);
        }

        #endregion

        #region GetItemExclusive

        #endregion

        #region ReleaseItemExclusive
        
        [Test]
        public void TestReleaseItemExclusive() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            provider.GetItemExclusive(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);
            provider.ReleaseItemExclusive(context, sessionId, lockId1);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var storeData = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);
           
            Assert.IsNotNull(storeData);
            Assert.IsFalse(locked2);
            Assert.AreEqual(SessionStateActions.None, actions2);
        }
        
        #endregion

        #region SetAndReleaseItemExclusive

        [Test]
        public void TestSetAndReleaseItemExclusiveWhenDoesNotExist() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();

            var storeData = provider.CreateNewStoreData(context, DefaultTimeout);
            storeData.Items["field"] = "value";
            provider.SetAndReleaseItemExclusive(context, sessionId, storeData, ObjectId.Empty, true);

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;
            var retrievedStoreData = provider.GetItem(context, sessionId, out locked, out lockAge, out lockId, out actions);

            Assert.IsNotNull(retrievedStoreData);
            Assert.AreEqual("value", retrievedStoreData.Items["field"]);
            Assert.IsFalse(locked);
            Assert.AreEqual(SessionStateActions.None, actions);
        }

        [Test]
        public void TestSetAndReleaseItemExclusiveWhenExists() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            var storeData = provider.GetItem(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);

            Assert.IsNotNull(storeData);
            storeData.Items["field"] = "value";
            // GetItemExclusive does not lock the item.
            provider.SetAndReleaseItemExclusive(context, sessionId, storeData, lockId1, false);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var retrievedStoreData = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);

            Assert.IsNotNull(retrievedStoreData);
            Assert.AreEqual("value", retrievedStoreData.Items["field"]);
            Assert.IsFalse(locked2);
            Assert.AreEqual(SessionStateActions.None, actions2);
        }

        [Test]
        public void TestSetAndReleaseItemExclusiveWhenExistsAndLocked() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            // GetItemExclusive locks the item.
            var storeData = provider.GetItemExclusive(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);

            Assert.IsNotNull(storeData);
            storeData.Items["field"] = "value";
            provider.SetAndReleaseItemExclusive(context, sessionId, storeData, lockId1, false);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var retrievedStoreData = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);

            Assert.IsNotNull(retrievedStoreData);
            Assert.AreEqual("value", retrievedStoreData.Items["field"]);
            Assert.IsFalse(locked2);
            Assert.AreEqual(SessionStateActions.None, actions2);
        }

        [Test]
        public void TestSetAndReleaseItemExclusiveWhenExistsWithInvalidLockId() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            var storeData = provider.GetItem(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);

            Assert.IsNotNull(storeData);
            storeData.Items["field"] = "value";
            provider.SetAndReleaseItemExclusive(context, sessionId, storeData, ObjectId.GenerateNewId() /* Invalid lock id */, false);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var retrievedStoreData = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);

            Assert.IsNotNull(retrievedStoreData);
            Assert.IsNull(retrievedStoreData.Items["field"]);
            Assert.IsFalse(locked2);
            Assert.AreEqual(SessionStateActions.None, actions2);
        }

        #endregion

        #region RemoveItem

        [Test]
        public void TestRemoveItem() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, DefaultTimeout);

            bool locked1;
            TimeSpan lockAge1;
            object lockId1;
            SessionStateActions actions1;
            var storeData1 = provider.GetItem(context, sessionId, out locked1, out lockAge1, out lockId1, out actions1);

            provider.RemoveItem(context, sessionId, lockId1, storeData1);

            bool locked2;
            TimeSpan lockAge2;
            object lockId2;
            SessionStateActions actions2;
            var storeData2 = provider.GetItem(context, sessionId, out locked2, out lockAge2, out lockId2, out actions2);

            Assert.IsNull(storeData2);
            Assert.IsFalse(locked2);
        }

        #endregion

        #region ResetItemTimeout

        [Test]
        public void TestResetItemTimeout() {
            var config = new NameValueCollection(_sessionConfig);
            var provider = new MongoSessionStateStore();
            provider.Initialize(DefaultSessionName, config);

            var context = CreateHttpContext();
            var sessionId = GenerateSessionId();
            provider.CreateUninitializedItem(context, sessionId, 0);

            // Give the session a chance to expire.
            Thread.Sleep(100);

            // Now reset the timeout.
            provider.ResetItemTimeout(context, sessionId);

            bool locked;
            TimeSpan lockAge;
            object lockId;
            SessionStateActions actions;
            var storeData = provider.GetItem(context, sessionId, out locked, out lockAge, out lockId, out actions);

            // Give the lock a chance to accumulate age.
            Thread.Sleep(100);

            Assert.IsNotNull(storeData);
            Assert.IsFalse(locked);
            Assert.Greater(lockAge, TimeSpan.Zero);
            Assert.AreNotEqual(ObjectId.Empty, lockId);
        }

        #endregion

        #region "Helpers"

        private HttpContext CreateHttpContext() {
            var httpRequest = new HttpRequest("", "http://example/", "");
            var stringWriter = new StringWriter();
            var httpResponse = new HttpResponse(stringWriter);
            return new HttpContext(httpRequest, httpResponse);
        }

        private string GenerateSessionId() {
            return Guid.NewGuid().ToString();
        }

        #endregion

    }

}
