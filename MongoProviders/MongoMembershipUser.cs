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
using System.Web.Security;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalLiberationFront.MongoProviders {
    internal sealed class MongoMembershipUser {
        [BsonId]
        public ObjectId Id { get; set; }

        public string UserName { get; set; }
        public string Password { get; set; }
        public string PasswordSalt { get; set; }
        public MembershipPasswordFormat PasswordFormat { get; set; }
        public string PasswordQuestion { get; set; }
        public string PasswordAnswer { get; set; }
        public int FailedPasswordAttemptCount { get; set; }
        public DateTime FailedPasswordAttemptWindowStartDate { get; set; }
        public int FailedPasswordAnswerAttemptCount { get; set; }
        public DateTime FailedPasswordAnswerAttemptWindowStartDate { get; set; }
        public string Email { get; set; }
        public string Comment { get; set; }
        public bool IsApproved { get; set; }
        public bool IsLockedOut { get; set; }
        public DateTime CreationDate { get; set; }
        public DateTime LastLoginDate { get; set; }
        public DateTime LastActivityDate { get; set; }
        public DateTime LastPasswordChangedDate { get; set; }
        public DateTime LastLockedOutDate { get; set; }

        public MembershipUser ToMembershipUser(string providerName) {
            return new MembershipUser(providerName, 
                UserName,
                Id,
                Email,                
                PasswordQuestion, 
                Comment, 
                IsApproved,
                IsLockedOut, 
                CreationDate, 
                LastLoginDate,
                LastActivityDate,
                LastPasswordChangedDate,
                LastLockedOutDate);
        }
    }
}
