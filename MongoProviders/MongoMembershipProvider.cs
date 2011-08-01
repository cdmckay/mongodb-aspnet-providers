﻿#region License
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
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Hosting;
using System.Web.Security;
using DigitalLiberationFront.MongoProviders.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoProviders {
    public class MongoMembershipProvider : MembershipProvider {

        private bool _enablePasswordRetrieval;
        public override bool EnablePasswordRetrieval {
            get { return _enablePasswordRetrieval; }
        }

        private bool _enablePasswordReset;
        public override bool EnablePasswordReset {
            get { return _enablePasswordReset; }
        }

        private bool _requiresQuestionAndAnswer;
        public override bool RequiresQuestionAndAnswer {
            get { return _requiresQuestionAndAnswer; }
        }

        public override string ApplicationName { get; set; }

        private int _maxInvalidPasswordAttempts;
        public override int MaxInvalidPasswordAttempts {
            get { return _maxInvalidPasswordAttempts; }
        }

        private int _passwordAttemptWindow;
        public override int PasswordAttemptWindow {
            get { return _passwordAttemptWindow; }
        }

        private bool _requiresUniqueEmail;
        public override bool RequiresUniqueEmail {
            get { return _requiresUniqueEmail; }
        }

        private MembershipPasswordFormat _passwordFormat;
        public override MembershipPasswordFormat PasswordFormat {
            get { return _passwordFormat; }
        }

        private int _minRequiredPasswordLength;
        public override int MinRequiredPasswordLength {
            get { return _minRequiredPasswordLength; }
        }

        private int _minRequiredNonAlphanumericCharacters;
        public override int MinRequiredNonAlphanumericCharacters {
            get { return _minRequiredNonAlphanumericCharacters; }
        }

        private string _passwordStrengthRegularExpression;
        public override string PasswordStrengthRegularExpression {
            get { return _passwordStrengthRegularExpression; }
        }

        private string _name;
        public override string Name {
            get { return _name; }
        }

        public override string Description {
            get { return "MongoDB-backed Membership Provider"; }
        }

        private string _connectionString;
        private string _databaseName;

        private readonly object _initailizedLock = new object();
        private bool _initialized = false;

        public override void Initialize(string name, NameValueCollection config) {
            lock (_initailizedLock) {
                if (_initialized) {
                    throw new InvalidOperationException(ProviderResources.Membership_ProviderAlreadyInitialized);
                }
                _initialized = true;
            }

            if (name == null) {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0) {
                throw new ArgumentException(ProviderResources.Membership_ProviderNameHasZeroLength, "name");
            }
            _name = name;

            // Deal with the application name.
            var applicationName = config["applicationName"];
            if (string.IsNullOrEmpty(applicationName)) {
                applicationName = "/";
            } else if (applicationName.Contains('\0')) {
                throw new ProviderException(string.Format("Application name cannot contain the '{0}' character.", @"\0"));
            } else if (applicationName.Contains('$')) {
                throw new ProviderException(string.Format("Application name cannot contain the '{0}' character.", @"$"));
            }
            ApplicationName = applicationName ?? HostingEnvironment.ApplicationVirtualPath;

            // Get the rest of the parameters.
            _enablePasswordRetrieval = Convert.ToBoolean(config["enablePasswordRetrieval"] ?? "false");
            _enablePasswordReset = Convert.ToBoolean(config["enablePasswordReset"] ?? "false");
            _requiresQuestionAndAnswer = Convert.ToBoolean(config["requiresQuestionAndAnswer"] ?? "false");
            _maxInvalidPasswordAttempts = Convert.ToInt32(config["maxInvalidPasswordAttempts"] ?? "5");
            _passwordAttemptWindow = Convert.ToInt32(config["passwordAttemptWindow"] ?? "10");
            _requiresUniqueEmail = Convert.ToBoolean(config["requiresUniqueEmail"] ?? "true");
            _minRequiredPasswordLength = Convert.ToInt32(config["minRequiredPasswordLength"] ?? "1");
            _minRequiredNonAlphanumericCharacters = Convert.ToInt32(config["minRequiredNonAlphanumericCharacters"] ?? "1");
            _passwordStrengthRegularExpression = config["passwordStrengthRegularExpression"] ?? string.Empty;

            // Handle password format.
            var passwordFormat = config["passwordFormat"] ?? "hashed";
            switch (passwordFormat.ToLowerInvariant()) {
                case "hashed":
                    _passwordFormat = MembershipPasswordFormat.Hashed;
                    break;
                case "encrypted":
                    _passwordFormat = MembershipPasswordFormat.Encrypted;
                    break;
                case "clear":
                    _passwordFormat = MembershipPasswordFormat.Clear;
                    break;
                default:
                    throw new ProviderException(string.Format(ProviderResources.Membership_PasswordFormatNotSupported, passwordFormat));
            }

            bool passwordsAreIrretrievable = PasswordFormat == MembershipPasswordFormat.Hashed
                                             || PasswordFormat == MembershipPasswordFormat.Encrypted;

            if (_enablePasswordRetrieval && passwordsAreIrretrievable) {
                throw new ProviderException(ProviderResources.Membership_CannotRetrievedHashedOrEncryptedPasswords);
            }

            // Get the connection string.
            var connectionStringSettings = ConfigurationManager.ConnectionStrings[config["connectionStringName"]];
            _connectionString = connectionStringSettings != null
                                    ? connectionStringSettings.ConnectionString.Trim()
                                    : string.Empty;
            var mongoUrl = new MongoUrl(_connectionString);
            _databaseName = mongoUrl.DatabaseName;

            // Setup collections.
            var users = GetCollection<MongoMembershipUser>("users");
            if (!users.Exists()) {
                users.EnsureIndex(IndexKeys.Ascending("UserName"), IndexOptions.SetUnique(true));
                users.EnsureIndex(IndexKeys.Ascending("Email"));
            }
        }

        public override MembershipUser CreateUser(string userName, string password, string email, string passwordQuestion, string passwordAnswer,
            bool isApproved, object providerUserKey, out MembershipCreateStatus status) {

            if (userName != null) {
                userName = userName.Trim();
            }
            if (password != null) {
                password = password.Trim();
            }
            if (email != null) {
                email = email.Trim();
            }
            if (passwordQuestion != null) {
                passwordQuestion = passwordQuestion.Trim();
            }
            if (passwordAnswer != null) {
                passwordAnswer = passwordAnswer.Trim();
            }

            bool hasFailedValidation = false;
            status = MembershipCreateStatus.Success;

            if (string.IsNullOrWhiteSpace(userName) || userName.Contains(",")) {
                status = MembershipCreateStatus.InvalidUserName;
                hasFailedValidation = true;
            }
            if (string.IsNullOrWhiteSpace(password)) {
                status = MembershipCreateStatus.InvalidPassword;
                hasFailedValidation = true;
            }

            var passwordEventArgs = new ValidatePasswordEventArgs(userName, password, true);
            OnValidatingPassword(passwordEventArgs);
            if (passwordEventArgs.Cancel) {
                status = MembershipCreateStatus.InvalidPassword;
                hasFailedValidation = true;
                //} else if (RequiresUniqueEmail) {
                //    status = MembershipCreateStatus.DuplicateEmail;
                //    hasInvalidArgument = true;
            } else if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(passwordQuestion)) {
                status = MembershipCreateStatus.InvalidQuestion;
                hasFailedValidation = true;
            } else if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(passwordAnswer)) {
                status = MembershipCreateStatus.InvalidAnswer;
                hasFailedValidation = true;
            } else if (!ValidatePassword(password)) {
                status = MembershipCreateStatus.InvalidPassword;
                hasFailedValidation = true;
            }

            if (providerUserKey != null && !(providerUserKey is ObjectId)) {
                status = MembershipCreateStatus.InvalidProviderUserKey;
                hasFailedValidation = true;
            }
            if (providerUserKey == null) {
                providerUserKey = ObjectId.GenerateNewId();
            }

            MembershipUser oldUser = GetUser(userName, false);
            if (oldUser != null) {
                status = MembershipCreateStatus.DuplicateUserName;
                hasFailedValidation = true;
            }

            if (hasFailedValidation) {
                return null;
            }

            string passwordSalt = GeneratePasswordSalt();
            DateTime creationDate = DateTime.Now;

            var newUser = new MongoMembershipUser {
                Id = (ObjectId) providerUserKey,
                UserName = userName,
                Password = EncodePassword(password, _passwordFormat, passwordSalt),
                PasswordFormat = PasswordFormat,
                PasswordSalt = passwordSalt,
                PasswordQuestion = passwordQuestion,
                PasswordAnswer = EncodePassword(passwordAnswer, _passwordFormat, passwordSalt),
                FailedPasswordAttemptCount = 0,
                FailedPasswordAttemptWindowStartDate = creationDate,
                FailedPasswordAnswerAttemptCount = 0,
                FailedPasswordAnswerAttemptWindowStartDate = creationDate,
                Email = email,
                Comment = null,
                IsApproved = isApproved,
                IsLockedOut = false,
                CreationDate = creationDate,
                LastLoginDate = DateTime.MinValue,
                LastActivityDate = DateTime.MinValue,
                LastPasswordChangedDate = DateTime.MinValue,
                LastLockedOutDate = DateTime.MinValue
            };
            
            try {
                var users = GetCollection<MongoMembershipUser>("users");
                users.Insert(newUser, SafeMode.True);
            } catch (MongoSafeModeException e) {
                if (e.Message.Contains("_id_")) {
                    status = MembershipCreateStatus.DuplicateProviderUserKey;
                } else if (e.Message.Contains("UserName_1")) {
                    status = MembershipCreateStatus.DuplicateUserName;
                } else {
                    status = MembershipCreateStatus.ProviderError;
                }
                return null;
            }

            return GetUser(userName, false);
        }

        public override bool ChangePasswordQuestionAndAnswer(string username, string password, string newPasswordQuestion, string newPasswordAnswer) {
            if (username != null) {
                username = username.Trim();                
            }
            if (newPasswordQuestion != null) {
                newPasswordQuestion = newPasswordQuestion.Trim();
            }
            if (newPasswordAnswer != null) {
                newPasswordAnswer = newPasswordAnswer.Trim();
            }
            
            if (!ValidateUser(username, password)) {
                return false;
            }
            
            var user = GetMongoUser(username);
            if (user == null) {
                return false;
            }

            var query = Query.EQ("_id", user.Id);
            var update = Update
                .Set("PasswordQuestion", newPasswordQuestion)
                .Set("PasswordAnswer", EncodePassword(newPasswordAnswer, user.PasswordFormat, user.PasswordSalt));            

            try {
                var users = GetCollection<MongoMembershipUser>("users");
                users.Update(query, update, SafeMode.True);
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not change password question and answer: " + e.Message);
            }

            return true;
        }

        public override string GetPassword(string username, string answer) {
            throw new NotImplementedException();
        }

        public override bool ChangePassword(string username, string oldPassword, string newPassword) {
            throw new NotImplementedException();
        }

        public override string ResetPassword(string username, string answer) {
            throw new NotImplementedException();
        }

        public override void UpdateUser(MembershipUser user) {
            throw new NotImplementedException();
        }

        public override bool ValidateUser(string username, string password) {
            if (string.IsNullOrEmpty(username)) {
                return false;
            }

            var user = GetMongoUser(username);
            if (user == null || user.IsLockedOut) {
                return false;
            }

            var passwordCorrect = CheckPassword(password, user.Password, user.PasswordFormat, user.PasswordSalt);
            if (!passwordCorrect) {
                RecordFailedAttempt(user.Id, FailedAttemptType.Password);
                return false;
            } 
            
            if (!user.IsApproved) {
                return false;
            }            
            
            var query = Query.EQ("_id", user.Id);
            var now = DateTime.Now;
            var update = Update
                .Set("LastLoginDate", now)
                .Set("LastActivityDate", now);
                        
            try {
                var users = GetCollection<MongoMembershipUser>("users");
                users.Update(query, update, SafeMode.True);
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not record failed attempt: " + e.Message);
            }

            return true;
        }

        public override bool UnlockUser(string userName) {
            throw new NotImplementedException();
        }        

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline) {            
            ObjectId id;
            if (providerUserKey is ObjectId) {
                id = (ObjectId) providerUserKey;
            } else if (ObjectId.TryParse(providerUserKey.ToString(), out id)) {
                // Assigned in parse.                                    
            } else {
                return null;
            }
            
            var query = Query.EQ("_id", id);

            var users = GetCollection<MongoMembershipUser>("users");
            MongoMembershipUser user;
            if (userIsOnline) {
                var update = Update.Set("LastActivityDate", DateTime.Now);
                var result = users.FindAndModify(query, SortBy.Null, update, true);
                user = result.GetModifiedDocumentAs<MongoMembershipUser>();
            } else {
                user = users.FindOneAs<MongoMembershipUser>(query);
            }

            return user != null ? user.ToMembershipUser(Name) : null;
        }        

        public override MembershipUser GetUser(string userName, bool userIsOnline) {
            if (userName == null) {
                throw new ArgumentNullException("userName");
            }
            if (string.IsNullOrWhiteSpace(userName)) {
                return null;
            }
            
            var query = Query.EQ("UserName", userName);

            var users = GetCollection<MongoMembershipUser>("users");
            MongoMembershipUser user;
            if (userIsOnline) {
                var update = Update.Set("LastActivityDate", DateTime.Now);
                var result = users.FindAndModify(query, SortBy.Null, update, true);
                user = result.GetModifiedDocumentAs<MongoMembershipUser>();
            } else {
                user = users.FindOneAs<MongoMembershipUser>(query);
            }

            return user != null ? user.ToMembershipUser(Name) : null;
        }        

        public override string GetUserNameByEmail(string email) {
            var users = GetCollection<MongoMembershipUser>("users");
            var result = users.Find(Query.EQ("Email", email)).SetSortOrder(SortBy.Ascending("UserName"));
            var user = result.FirstOrDefault();

            return user != null ? user.UserName : null;
        }        

        public override bool DeleteUser(string username, bool deleteAllRelatedData) {
            throw new NotImplementedException();
        }

        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords) {
            throw new NotImplementedException();
        }        

        public override int GetNumberOfUsersOnline() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// An generalized version of the <see cref="FindUsersByName"/> and <see cref="FindUsersByEmail"/> methods that:
        /// <list type="bullet">
        /// <item><description>allows arbitrary fields to be searched,</description></item>
        /// <item><description>allows more granular control over chunk of records is returned,</description></item>
        /// <item><description>returns a more LINQ-friendly <see cref="IEnumerable{MembershipUser}"/> object.</description></item>
        /// </list>
        /// </summary>
        /// <param name="query"></param>
        /// <param name="sortBy"></param>
        /// <param name="skip"></param>
        /// <param name="take"></param>
        /// <param name="totalRecords"></param>
        /// <returns></returns>
        public IEnumerable<MembershipUser> FindUsers(IMongoQuery query, IMongoSortBy sortBy, int skip, int take, out int totalRecords) {
            if (query == null) {
                throw new ArgumentNullException("query");
            }
            if (skip < 0) {
                throw new ArgumentException(ProviderResources.Membership_SkipMustBeGreaterThanOrEqualToZero, "skip");
            }
            if (take < 0) {
                throw new ArgumentException(ProviderResources.Membership_TakeMustBeGreaterThanOrEqualToZero, "take");
            }
           
            var users = GetCollection<MongoMembershipUser>("users");
            var matches = users.Find(query).SetSkip(skip).SetLimit(take);
            if (sortBy != null) {
                matches.SetSortOrder(sortBy);
            }
            totalRecords = matches.Count();
            return matches.Select(m => m.ToMembershipUser(Name));
        }

        public override MembershipUserCollection FindUsersByName(string userNameToMatch, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                throw new ArgumentException(ProviderResources.Membership_PageIndexMustBeGreaterThanOrEqualToZero, "pageIndex");
            }
            if (pageSize < 0) {
                throw new ArgumentException(ProviderResources.Membership_PageSizeMustBeGreaterThanOrEqualToZero, "pageSize");
            }

            var users = FindUsers(Query.Matches("UserName", userNameToMatch), SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            var collection = new MembershipUserCollection();
            foreach (var u in users) {
                collection.Add(u);    
            }
            return collection;
        }

        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                throw new ArgumentException(ProviderResources.Membership_PageIndexMustBeGreaterThanOrEqualToZero, "pageIndex");
            }
            if (pageSize < 0) {
                throw new ArgumentException(ProviderResources.Membership_PageSizeMustBeGreaterThanOrEqualToZero, "pageSize");
            }

            var users = FindUsers(Query.Matches("Email", emailToMatch), SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            var collection = new MembershipUserCollection();
            foreach (var u in users) {
                collection.Add(u);
            }
            return collection;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TDefaultDocument"></typeparam>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        private MongoCollection<TDefaultDocument> GetCollection<TDefaultDocument>(string collectionName) {
            var server = MongoServer.Create(_connectionString);
            var database = server.GetDatabase(_databaseName);
            return database.GetCollection<TDefaultDocument>(ApplicationName + "." + collectionName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(ObjectId id) {
            var users = GetCollection<MongoMembershipUser>("users");
            return users.FindOneAs<MongoMembershipUser>(Query.EQ("_id", id));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(string username) {
            var users = GetCollection<MongoMembershipUser>("users");
            return users.FindOneAs<MongoMembershipUser>(Query.EQ("UserName", username));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        private bool ValidatePassword(string password) {
            bool passwordIsLongEnough = password.Length >= MinRequiredPasswordLength;
            bool passwordHasEnoughAlphnumericCharacters =
                password.Where(char.IsLetterOrDigit).Sum(ch => 1) >= MinRequiredNonAlphanumericCharacters;
            bool passwordIsStrongEnough = PasswordStrengthRegularExpression.Length == 0
                || Regex.IsMatch(password, PasswordStrengthRegularExpression);

            return passwordIsLongEnough
                && passwordHasEnoughAlphnumericCharacters
                && passwordIsStrongEnough;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static string GeneratePasswordSalt() {
            var buffer = new byte[16];
            (new RNGCryptoServiceProvider()).GetBytes(buffer);
            return Convert.ToBase64String(buffer);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="password"></param>
        /// <param name="passwordFormat"></param>
        /// <param name="passwordSalt"></param>
        /// <returns></returns>
        private string EncodePassword(string password, MembershipPasswordFormat passwordFormat, string passwordSalt) {
            if (passwordFormat == MembershipPasswordFormat.Clear || string.IsNullOrEmpty(password)) {
                return password;
            }

            byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
            byte[] saltBytes = Convert.FromBase64String(passwordSalt);
            byte[] combinedBytes = new byte[saltBytes.Length + passwordBytes.Length];
            byte[] encodedBytes;

            Buffer.BlockCopy(saltBytes, 0, combinedBytes, 0, saltBytes.Length);
            Buffer.BlockCopy(passwordBytes, 0, combinedBytes, saltBytes.Length, passwordBytes.Length);

            switch (passwordFormat) {
                case MembershipPasswordFormat.Hashed:
                    var algorithm = HashAlgorithm.Create(Membership.HashAlgorithmType);
                    encodedBytes = algorithm.ComputeHash(combinedBytes);
                    break;

                case MembershipPasswordFormat.Encrypted:
                    encodedBytes = EncryptPassword(combinedBytes);
                    break;

                default:
                    throw new ProviderException(string.Format("Unrecogized password format: {0}", passwordFormat));
            }

            return Convert.ToBase64String(encodedBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="password"></param>
        /// <param name="storedPassword"></param>
        /// <param name="passwordFormat"></param>
        /// <param name="passwordSalt"></param>
        /// <returns></returns>
        private bool CheckPassword(string password, string storedPassword, MembershipPasswordFormat passwordFormat, string passwordSalt) {
            var encodedPassword = EncodePassword(password, passwordFormat, passwordSalt);
            return encodedPassword == storedPassword;
        }

        /// <summary>
        /// The possible failed attempt types.
        /// </summary>
        private enum FailedAttemptType {
            Password,
            PasswordAnswer
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="failedAttemptType"></param>
        private void RecordFailedAttempt(ObjectId id, FailedAttemptType failedAttemptType) {
            var user = GetMongoUser(id);
            if (user == null) {
                throw new ProviderException(string.Format("Could not record failed attempt: no user exists with id '{0}'", id));
            }

            int attemptCount;
            DateTime attemptWindowEndDate;
            string attemptCountField, attemptWindowStartDate;
            switch (failedAttemptType) {
                case FailedAttemptType.Password:
                    attemptCount = user.FailedPasswordAttemptCount;
                    attemptWindowEndDate = user.FailedPasswordAttemptWindowStartDate.AddMinutes(PasswordAttemptWindow);
                    attemptCountField = "FailedPasswordAttemptCount";
                    attemptWindowStartDate = "FailedPasswordAttemptWindowStartDate";
                    break;
                case FailedAttemptType.PasswordAnswer:
                    attemptCount = user.FailedPasswordAnswerAttemptCount;
                    attemptWindowEndDate = user.FailedPasswordAnswerAttemptWindowStartDate.AddMinutes(PasswordAttemptWindow);
                    attemptCountField = "FailedPasswordAnswerAttemptCount";
                    attemptWindowStartDate = "FailedPasswordAnswerAttemptWindowStartDate";
                    break;
                default:
                    throw new ProviderException(string.Format("Unknown failed attempt type: {0}", failedAttemptType));                    
            }

            var users = GetCollection<MongoMembershipUser>("users");
            try {                
                var query = Query.EQ("_id", id);
                if (attemptCount == 0 || DateTime.Now > attemptWindowEndDate) {
                    var update = Update
                        .Set(attemptCountField, 1)
                        .Set(attemptWindowStartDate, DateTime.Now);
                    users.Update(query, update, SafeMode.True);
                }
                else {
                    attemptCount++;
                    if (attemptCount >= MaxInvalidPasswordAttempts) {
                        var update = Update
                            .Set("IsLockedOut", true)
                            .Set("LastLockedOutDate", DateTime.Now);
                        users.Update(query, update, SafeMode.True);
                    }
                    else {
                        var update = Update.Inc(attemptCountField, 1);
                        users.Update(query, update);
                    }
                }
            } catch (MongoSafeModeException e) {
                throw new ProviderException("Could not record failed attempt: " + e.Message);
            }
        }

    }
}