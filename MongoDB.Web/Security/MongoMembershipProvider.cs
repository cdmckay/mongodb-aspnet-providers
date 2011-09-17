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
using System.Configuration.Provider;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Security;
using DigitalLiberationFront.MongoDB.Web.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    public class MongoMembershipProvider : MembershipProvider {

        /// <summary>
        /// The length of the password salt.
        /// </summary>
        private const int PasswordSaltLength = 16;

        /// <summary>
        /// When resetting a password, the length it should it be.
        /// </summary>
        private const int NewPasswordLength = 8;

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

        private bool _enableTrace;
        private TraceSource _traceSource;

        private string _connectionString;
        private string _databaseName;
        private SafeMode _safeMode;

        public override void Initialize(string name, NameValueCollection config) {
            if (name == null) {
                throw new ArgumentNullException("name");
            }
            if (name.Length == 0) {
                throw new ArgumentException(ProviderResources.ProviderNameHasZeroLength, "name");
            }
            if (string.IsNullOrWhiteSpace(config["description"])) {
                config.Remove("description");
                config["description"] = "MongoDB Membership Provider";
            }

            // Initialize the base class.
            base.Initialize(name, config);

            _enableTrace = Convert.ToBoolean(config["enableTrace"] ?? "false");
            if (_enableTrace) {
                _traceSource = new TraceSource(GetType().Name, SourceLevels.All);
            }

            // Deal with the application name.           
            ApplicationName = ProviderHelper.ResolveApplicationName(config);

            // Get the rest of the parameters.
            _enablePasswordRetrieval = Convert.ToBoolean(config["enablePasswordRetrieval"] ?? "false");
            _enablePasswordReset = Convert.ToBoolean(config["enablePasswordReset"] ?? "false");
            _requiresQuestionAndAnswer = Convert.ToBoolean(config["requiresQuestionAndAnswer"] ?? "false");
            _maxInvalidPasswordAttempts = Convert.ToInt32(config["maxInvalidPasswordAttempts"] ?? "5");
            _passwordAttemptWindow = Convert.ToInt32(config["passwordAttemptWindow"] ?? "10");
            _requiresUniqueEmail = Convert.ToBoolean(config["requiresUniqueEmail"] ?? "true");
            _minRequiredPasswordLength = Convert.ToInt32(config["minRequiredPasswordLength"] ?? "1");
            _minRequiredNonAlphanumericCharacters = Convert.ToInt32(config["minRequiredNonalphanumericCharacters"] ?? "1");
            _passwordStrengthRegularExpression = config["passwordStrengthRegularExpression"] ?? string.Empty;            

            // Make sure that passwords are at least 1 character long.
            if (_minRequiredPasswordLength <= 0) {
                var message = ProviderResources.MinimumPasswordLengthMustBeGreaterThanZero;
                throw TraceException("Initialize", new ProviderException(message));
            }

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
                    var message = string.Format(ProviderResources.PasswordFormatNotSupported, passwordFormat);
                    throw TraceException("Initialize", new ProviderException(message));
            }

            bool passwordsAreIrretrievable = PasswordFormat == MembershipPasswordFormat.Hashed;
            if (_enablePasswordRetrieval && passwordsAreIrretrievable) {
                var message = string.Format(ProviderResources.CannotRetrievePasswords, PasswordFormat);
                throw TraceException("Initialize", new ProviderException(message));
            }            

            // Get the connection string.
            _connectionString = ProviderHelper.ResolveConnectionString(config);

            // Get the database name.
            var mongoUrl = new MongoUrl(_connectionString);
            _databaseName = mongoUrl.DatabaseName;

            _safeMode = ProviderHelper.GenerateSafeMode(config);

            // Initialize collections.
            ProviderHelper.InitializeCollections(ApplicationName, _connectionString, _databaseName, _safeMode);
        }

        public override MembershipUser CreateUser(string userName, string password, string email, string passwordQuestion, string passwordAnswer,
            bool isApproved, object providerUserKey, out MembershipCreateStatus status) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("CreateUser", new ArgumentException(message, "userName"));
            }
            if (string.IsNullOrWhiteSpace(password)) {
                var message = ProviderResources.PasswordCannotBeNullOrWhiteSpace;
                throw TraceException("CreateUser", new ArgumentException(message, "password"));
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
            } else if (RequiresUniqueEmail && EmailIsDuplicate(email)) {
                status = MembershipCreateStatus.DuplicateEmail;
                hasFailedValidation = true;
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

            var id = ConvertProviderUserKeyToObjectId(providerUserKey);
            if (providerUserKey != null && !id.HasValue) {
                status = MembershipCreateStatus.InvalidProviderUserKey;
                hasFailedValidation = true;
            }
            if (providerUserKey == null) {
                providerUserKey = ObjectId.GenerateNewId();
            }

            var oldUser = GetUser(userName, false);
            if (oldUser != null) {
                status = MembershipCreateStatus.DuplicateUserName;
                hasFailedValidation = true;
            }

            if (hasFailedValidation) {
                return null;
            }

            var passwordSalt = GeneratePasswordSalt();
            var creationDate = DateTime.Now;

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
                var users = GetUserCollection();
                users.Insert(newUser);
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

        public override bool ChangePassword(string userName, string oldPassword, string newPassword) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePassword", new ArgumentException(message, "userName"));
            }
            if (string.IsNullOrWhiteSpace(oldPassword)) {
                var message = ProviderResources.OldPasswordCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePassword", new ArgumentException(message, "oldPassword"));
            }
            if (string.IsNullOrWhiteSpace(newPassword)) {
                var message = ProviderResources.NewPasswordCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePassword", new ArgumentException(message, "newPassword"));
            }

            if (!ValidateUser(userName, oldPassword)) {
                return false;
            }

            var passwordEventArgs = new ValidatePasswordEventArgs(userName, newPassword, true);
            OnValidatingPassword(passwordEventArgs);
            if (passwordEventArgs.Cancel) {
                var message = ProviderResources.PasswordChangeCancelled;
                throw TraceException("ChangePassword", new ProviderException(message));
            }
            if (!ValidatePassword(newPassword)) {
                return false;
            }

            var user = GetMongoUser(userName);
            if (user == null) {
                return false;
            }

            var query = Query.EQ("_id", user.Id);
            var update = Update
                .Set("Password", EncodePassword(newPassword, user.PasswordFormat, user.PasswordSalt))
                .Set("LastPasswordChangedDate", SerializationHelper.SerializeDateTime(DateTime.Now));

            try {
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotChangePassword;
                throw TraceException("ChangePassword", new ProviderException(message, e));
            }

            return true;
        }

        public override bool ChangePasswordQuestionAndAnswer(string userName, string password, string newPasswordQuestion, string newPasswordAnswer) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePasswordQuestionAndAnswer", new ArgumentException(message, "userName"));
            }
            if (string.IsNullOrWhiteSpace(password)) {
                var message = ProviderResources.PasswordCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePasswordQuestionAndAnswer", new ArgumentException(message, "password"));
            }
            if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(newPasswordQuestion)) {
                var message = ProviderResources.NewPasswordQuestionCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePasswordQuestionAndAnswer", new ArgumentException(message, "newPasswordQuestion"));
            }

            if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(newPasswordAnswer)) {
                var message = ProviderResources.NewPasswordAnswerCannotBeNullOrWhiteSpace;
                throw TraceException("ChangePasswordQuestionAndAnswer", new ArgumentException(message, "newPasswordAnswer"));
            }

            newPasswordQuestion = newPasswordQuestion.Trim();
            newPasswordAnswer = newPasswordAnswer.Trim();
            
            if (!ValidateUser(userName, password)) {
                return false;
            }

            var user = GetMongoUser(userName);
            if (user == null) {
                return false;
            }

            var query = Query.EQ("_id", user.Id);
            var update = Update
                .Set("PasswordQuestion", newPasswordQuestion)
                .Set("PasswordAnswer", EncodePassword(newPasswordAnswer, user.PasswordFormat, user.PasswordSalt));

            try {
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotChangePasswordQuestionAndAnswer;                
                throw TraceException("ChangePasswordQuestionAndAnswer", new ProviderException(message, e));
            }

            return true;
        }

        public override string GetPassword(string userName, string answer) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("GetPassword", new ArgumentException(message, "userName"));
            }
            if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(answer)) {
                var message = ProviderResources.PasswordAnswerCannotBeNullOrWhiteSpace;
                throw TraceException("GetPassword", new ArgumentException(message, "answer"));
            }
            if (!EnablePasswordRetrieval) {
                var message = ProviderResources.PasswordRetrievalIsDisabled;
                throw TraceException("GetPassword", new ProviderException(message));
            }
            
            var user = GetMongoUser(userName);
            if (user == null) {
                var message = ProviderResources.CouldNotFindUser;
                throw TraceException("GetPassword", new ProviderException(message));
            }
            if (user.IsLockedOut) {
                var message = ProviderResources.UserIsLockedOut;
                throw TraceException("GetPassword", new ProviderException(message));
            }

            bool authenticated = CheckPassword(answer, user.PasswordAnswer, user.PasswordFormat, user.PasswordSalt);
            if (RequiresQuestionAndAnswer && !authenticated) {
                HandleFailedAttempt(user.Id, FailedAttemptType.PasswordAnswer);
                var message = ProviderResources.IncorrectPasswordAnswer;
                throw TraceException("GetPassword", new MembershipPasswordException(message));
            }

            return DecodePassword(user.Password, user.PasswordFormat);
        }

        public override string ResetPassword(string userName, string answer) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("ResetPassword", new ArgumentException(message, "userName"));
            }
            if (RequiresQuestionAndAnswer && string.IsNullOrWhiteSpace(answer)) {
                var message = ProviderResources.PasswordAnswerCannotBeNullOrWhiteSpace;
                throw TraceException("ResetPassword", new ArgumentException(message, "answer"));
            }
            if (!EnablePasswordReset) {
                var message = ProviderResources.PasswordResetIsDisabled;
                throw TraceException("ResetPassword", new ProviderException(message));
            }

            var user = GetMongoUser(userName);
            if (user == null) {
                var message = ProviderResources.CouldNotFindUser;
                throw TraceException("ResetPassword", new ProviderException(message));
            }
            if (user.IsLockedOut) {
                var message = ProviderResources.UserIsLockedOut;
                throw TraceException("ResetPassword", new ProviderException(message));
            }

            bool authenticated = CheckPassword(answer, user.PasswordAnswer, user.PasswordFormat, user.PasswordSalt);
            if (RequiresQuestionAndAnswer && !authenticated) {
                HandleFailedAttempt(user.Id, FailedAttemptType.PasswordAnswer);
                var message = ProviderResources.IncorrectPasswordAnswer;
                throw TraceException("ResetPassword", new MembershipPasswordException(message));
            }

            var newPassword = Membership.GeneratePassword(NewPasswordLength, MinRequiredNonAlphanumericCharacters);
            var passwordEventArgs = new ValidatePasswordEventArgs(userName, newPassword, true);
            OnValidatingPassword(passwordEventArgs);
            if (passwordEventArgs.Cancel) {
                var message = ProviderResources.PasswordChangeCancelled;
                throw TraceException("ResetPassword", new ProviderException(message));
            }

            var query = Query.EQ("_id", user.Id);
            var update = Update
                .Set("Password", EncodePassword(newPassword, user.PasswordFormat, user.PasswordSalt))
                .Set("LastPasswordChangedDate", SerializationHelper.SerializeDateTime(DateTime.Now));

            try {
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotResetPassword;
                throw TraceException("ResetPassword", new ProviderException(message, e));
            }

            return newPassword;
        }

        public override void UpdateUser(MembershipUser user) {
            if (user == null) {
                throw TraceException("UpdateUser", new ArgumentNullException("user"));
            }

            var id = ConvertProviderUserKeyToObjectId(user.ProviderUserKey);
            if (!id.HasValue) {
                var message = ProviderResources.UserDoesNotExist;
                throw TraceException("UpdateUser", new ProviderException(message));
            }

            if (RequiresUniqueEmail && EmailIsDuplicate(user.Email)) {
                var message = ProviderResources.UserHasADuplicateEmailAddress;
                throw TraceException("UpdateUser", new ProviderException(message));
            }

            var query = Query.EQ("_id", id.Value);
            var update = Update
                .Set("UserName", user.UserName)
                .Set("Email", user.Email)
                .Set("Comment", user.Comment)
                .Set("IsApproved", user.IsApproved)
                .Set("LastLoginDate", SerializationHelper.SerializeDateTime(user.LastLoginDate))
                .Set("LastActivityDate", SerializationHelper.SerializeDateTime(user.LastActivityDate));

            try {
                var users = GetUserCollection();
                var result = users.Update(query, update);
                if (result.DocumentsAffected == 0) {
                    var providerException = new ProviderException(ProviderResources.UserDoesNotExist);
                    TraceException("UpdateUser", providerException);
                    throw providerException;
                }
            } catch (MongoSafeModeException e) {
                string message;
                if (e.Message.Contains("UserName_1")) {
                    message = ProviderResources.UserHasADuplicateName;
                } else {
                    message = ProviderResources.CouldNotUpdateUser;
                }
                throw TraceException("UpdateUser", new ProviderException(message, e));
            }
        }

        public override bool ValidateUser(string userName, string password) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("ValidateUser", new ArgumentException(message, "userName"));
            }
            if (string.IsNullOrWhiteSpace(password)) {
                var message = ProviderResources.PasswordCannotBeNullOrWhiteSpace;
                throw TraceException("ValidateUser", new ArgumentException(message, "password"));
            }

            var user = GetMongoUser(userName);
            if (user == null || user.IsLockedOut) {
                return false;
            }

            var passwordCorrect = CheckPassword(password, user.Password, user.PasswordFormat, user.PasswordSalt);
            if (!passwordCorrect) {
                HandleFailedAttempt(user.Id, FailedAttemptType.Password);
                return false;
            }

            if (!user.IsApproved) {
                return false;
            }

            var query = Query.EQ("_id", user.Id);
            var now = SerializationHelper.SerializeDateTime(DateTime.Now);
            var update = Update
                .Set("LastLoginDate", now)
                .Set("LastActivityDate", now);

            try {
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotUpdateUser;
                throw TraceException("ValidateUser", new ProviderException(message, e));
            }

            return true;
        }

        public override bool UnlockUser(string userName) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("UnlockUser", new ArgumentException(message, "userName"));
            }

            var user = GetMongoUser(userName);
            if (user == null) {
                return false;
            }

            // Implementation of this method should set the IsLockedOut property to false, 
            // set the LastLockoutDate property to the current date, 
            // and reset any counters that you use to track the number of failed log in attempts 
            // and so forth.
            var query = Query.EQ("_id", user.Id);
            var update = Update
                .Set("IsLockedOut", false)
                .Set("LastLockedOutDate", SerializationHelper.SerializeDateTime(DateTime.Now))
                .Set("FailedPasswordAttemptCount", 0)
                .Set("FailedPasswordAnswerAttemptCount", 0);

            try {
                var users = GetUserCollection();
                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotUpdateUser;
                throw TraceException("UnlockUser", new ProviderException(message, e));
            }

            return true;
        }

        public override MembershipUser GetUser(object providerUserKey, bool userIsOnline) {
            if (providerUserKey == null) {
                throw TraceException("GetUser", new ArgumentNullException("providerUserKey"));
            }

            var id = ConvertProviderUserKeyToObjectId(providerUserKey);
            if (!id.HasValue) {
                return null;
            }

            var query = Query.EQ("_id", id.Value);

            var users = GetUserCollection();
            MongoMembershipUser user;
            if (userIsOnline) {
                var update = Update.Set("LastActivityDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                var result = users.FindAndModify(query, SortBy.Null, update, returnNew: true);
                user = result.GetModifiedDocumentAs<MongoMembershipUser>();
            } else {
                user = users.FindOneAs<MongoMembershipUser>(query);
            }

            return user != null ? user.ToMembershipUser(Name) : null;
        }

        public override MembershipUser GetUser(string userName, bool userIsOnline) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("GetUser", new ArgumentException(message, "userName"));
            }

            var query = Query.EQ("UserName", userName);

            var users = GetUserCollection();
            MongoMembershipUser user;
            if (userIsOnline) {
                var update = Update.Set("LastActivityDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                var result = users.FindAndModify(query, SortBy.Null, update, returnNew: true);
                user = result.GetModifiedDocumentAs<MongoMembershipUser>();
            } else {
                user = users.FindOneAs<MongoMembershipUser>(query);
            }

            return user != null ? user.ToMembershipUser(Name) : null;
        }

        public override string GetUserNameByEmail(string email) {
            if (email != null) {
                email = email.Trim();
            }

            var users = GetUserCollection();
            var result = users.Find(Query.EQ("Email", email)).SetSortOrder(SortBy.Ascending("UserName"));
            var user = result.FirstOrDefault();

            return user != null ? user.UserName : null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <param name="deleteAllRelatedData">This parameter is not applicable for this provider,
        /// as all related data is part of the user record and will be deleted with the user regardless.</param>
        /// <returns></returns>
        public override bool DeleteUser(string userName, bool deleteAllRelatedData) {
            if (string.IsNullOrWhiteSpace(userName)) {
                var message = ProviderResources.UserNameCannotBeNullOrWhiteSpace;
                throw TraceException("DeleteUser", new ArgumentException(message, "userName"));
            }

            var user = GetMongoUser(userName);
            if (user == null) {
                return false;
            }           

            var query = Query.EQ("_id", user.Id);

            try {
                var users = GetUserCollection();
                users.Remove(query);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotRemoveUser;
                throw TraceException("DeleteUser", new ProviderException(message, e));
            }

            return true;
        }        

        public override int GetNumberOfUsersOnline() {            
            var windowStartDate = DateTime.Now.AddMinutes(-Membership.UserIsOnlineTimeWindow);
            var query = Query.GT("LastActivityDate.Ticks", windowStartDate.Ticks);

            int numberOfUsersOnline;
            try {
                var users = GetUserCollection();
                numberOfUsersOnline = users.Count(query);                
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotCountUsers;
                throw TraceException("GetNumberOfUsersOnline", new ProviderException(message, e));
            }
            return numberOfUsersOnline;
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
            if (skip < 0) {
                var message = ProviderResources.SkipMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsers", new ArgumentException(message, "skip"));
            }
            if (take < 0) {
                var message = ProviderResources.TakeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsers", new ArgumentException(message, "take"));
            }            

            var users = GetUserCollection();            
            var matches = users.Find(query).SetSkip(skip).SetLimit(take);
            if (sortBy != null) {
                matches.SetSortOrder(sortBy);
            }
            totalRecords = matches.Count();
            return matches.Select(m => m.ToMembershipUser(Name));
        }

        public override MembershipUserCollection FindUsersByName(string userNameToMatch, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsersByName", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsersByName", new ArgumentException(message, "pageSize"));
            }

            var users = FindUsers(Query.Matches("UserName", userNameToMatch), SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertMembershipUserEnumerableToCollection(users);
        }

        public override MembershipUserCollection FindUsersByEmail(string emailToMatch, int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsersByEmail", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("FindUsersByEmail", new ArgumentException(message, "pageSize"));
            }

            var users = FindUsers(Query.Matches("Email", emailToMatch), SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertMembershipUserEnumerableToCollection(users);
        }

        public override MembershipUserCollection GetAllUsers(int pageIndex, int pageSize, out int totalRecords) {
            if (pageIndex < 0) {
                var message = ProviderResources.PageIndexMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllUsers", new ArgumentException(message, "pageIndex"));
            }
            if (pageSize < 0) {
                var message = ProviderResources.PageSizeMustBeGreaterThanOrEqualToZero;
                throw TraceException("GetAllUsers", new ArgumentException(message, "pageSize"));
            }

            var users = FindUsers(Query.Null, SortBy.Null, pageIndex * pageSize, pageSize, out totalRecords);
            return ConvertMembershipUserEnumerableToCollection(users);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private MongoCollection<MongoMembershipUser> GetUserCollection() {
            return ProviderHelper.GetCollectionAs<MongoMembershipUser>(ApplicationName, _connectionString, _databaseName, _safeMode, "users");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(ObjectId id) {
            return ProviderHelper.GetMongoUser(_traceSource, GetUserCollection(), id);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="userName"></param>
        /// <returns></returns>
        private MongoMembershipUser GetMongoUser(string userName) {
            return ProviderHelper.GetMongoUser(_traceSource, GetUserCollection(), userName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="users"></param>
        /// <returns></returns>
        private static MembershipUserCollection ConvertMembershipUserEnumerableToCollection(IEnumerable<MembershipUser> users) {
            var collection = new MembershipUserCollection();
            foreach (var u in users) {
                collection.Add(u);
            }
            return collection;
        }

        /// <summary>
        /// Attempts to convert an <see cref="object"/> to an <see cref="ObjectId"/>.
        /// </summary>
        /// <param name="providerUserKey">An <see cref="ObjectId"/> or a <see cref="string"/> equivalent of one.</param>
        /// <returns>The <see cref="ObjectId"/>, or null.</returns>
        private static ObjectId? ConvertProviderUserKeyToObjectId(object providerUserKey) {
            if (providerUserKey == null) {
                return null;
            }
            if (providerUserKey is ObjectId) {
                return (ObjectId) providerUserKey;
            }

            ObjectId id;
            if (ObjectId.TryParse(providerUserKey.ToString(), out id)) {
                return id;
            }

            return null;
        }

        /// <summary>
        /// Checks if the email address is a duplicate.
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        private bool EmailIsDuplicate(string email) {
            var query = Query.EQ("Email", email);
            var users = GetUserCollection();
            var duplicates = users.Count(query);
            return duplicates > 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="password"></param>
        /// <returns></returns>
        private bool ValidatePassword(string password) {
            bool passwordIsLongEnough = password.Length >= MinRequiredPasswordLength;
            bool passwordHasEnoughAlphnumericCharacters =
                password.Where(ch => !char.IsLetterOrDigit(ch)).Sum(ch => 1) >= MinRequiredNonAlphanumericCharacters;
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
            var buffer = new byte[PasswordSaltLength];
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
                    var message = string.Format(ProviderResources.PasswordFormatNotSupported, passwordFormat);
                    throw TraceException("EncodePassword", new ProviderException(message));
            }

            return Convert.ToBase64String(encodedBytes);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="encodedPassword"></param>
        /// <param name="passwordFormat"></param>
        /// <returns></returns>
        private string DecodePassword(string encodedPassword, MembershipPasswordFormat passwordFormat) {
            string password;
            switch (passwordFormat) {
                case MembershipPasswordFormat.Clear:
                    password = encodedPassword;
                    break;
                case MembershipPasswordFormat.Encrypted:
                    // Grab the salt + password and lop off the salt (16 bytes).
                    var combinedBytes = DecryptPassword(Convert.FromBase64String(encodedPassword));
                    password = Encoding.Unicode.GetString(combinedBytes,
                        PasswordSaltLength,
                        combinedBytes.Length - PasswordSaltLength);
                    break;
                default:
                    var message = ProviderResources.CannotDecodePassword;
                    throw TraceException("DecodePassword", new ProviderException(message));
            }
            return password;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="password"></param>
        /// <param name="preEncodedPassword"></param>
        /// <param name="passwordFormat"></param>
        /// <param name="passwordSalt"></param>
        /// <returns></returns>
        private bool CheckPassword(string password, string preEncodedPassword, MembershipPasswordFormat passwordFormat, string passwordSalt) {
            var encodedPassword = EncodePassword(password, passwordFormat, passwordSalt);
            return encodedPassword == preEncodedPassword;
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
        private void HandleFailedAttempt(ObjectId id, FailedAttemptType failedAttemptType) {
            var user = GetMongoUser(id);
            if (user == null) {
                var message = ProviderResources.UserDoesNotExist;
                throw TraceException("HandleFailedAttempt", new ProviderException(message));
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
                    throw TraceException("HandleFailedAttempt", new ArgumentOutOfRangeException("failedAttemptType"));
            }

            try {
                var users = GetUserCollection();
                var query = Query.EQ("_id", id);
                UpdateBuilder update;

                attemptCount++;
                if (attemptCount >= MaxInvalidPasswordAttempts) {
                    update = Update
                        .Set("IsLockedOut", true)
                        .Set("LastLockedOutDate", SerializationHelper.SerializeDateTime(DateTime.Now));
                } else {
                    if (attemptCount == 1 || DateTime.Now > attemptWindowEndDate) {
                        update = Update
                            .Set(attemptCountField, 1)
                            .Set(attemptWindowStartDate, SerializationHelper.SerializeDateTime(DateTime.Now));
                    } else {
                        update = Update.Inc(attemptCountField, 1);
                    }
                }

                users.Update(query, update);
            } catch (MongoSafeModeException e) {
                var message = ProviderResources.CouldNotUpdateUser;
                throw TraceException("HandleFailedAttempt", new ProviderException(message, e));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="methodName"></param>
        /// <param name="e"></param>
        private Exception TraceException(string methodName, Exception e) {
            return ProviderHelper.TraceException(_traceSource, methodName, e);            
        }

    }
}
