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

            provider.CreateUninitializedItem(null, Guid.NewGuid().ToString(), 20);
        }

        #endregion

        

    }

}
