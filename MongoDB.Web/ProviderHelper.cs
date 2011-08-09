using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Linq;
using System.Web.Hosting;
using DigitalLiberationFront.MongoDB.Web.Security.Resources;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    internal static class ProviderHelper {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static string ResolveApplicationName(NameValueCollection config) {
            var applicationName = config["applicationName"];
            if (string.IsNullOrEmpty(applicationName)) {
                applicationName = HostingEnvironment.ApplicationVirtualPath;
            } else if (applicationName.Contains('\0')) {
                throw new ProviderException(string.Format(ProviderResources.Common_ApplicationNameCannotContainCharacter, @"\0"));
            } else if (applicationName.Contains('$')) {
                throw new ProviderException(string.Format(ProviderResources.Common_ApplicationNameCannotContainCharacter, @"$"));
            }
            return applicationName;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static string ResolveConnectionString(NameValueCollection config) {
            var connectionStringSettings = ConfigurationManager.ConnectionStrings[config["connectionStringName"]];
            return connectionStringSettings != null ? connectionStringSettings.ConnectionString.Trim() : string.Empty;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="applicationName"></param>
        /// <param name="connectionString"></param>
        /// <param name="databaseName"></param>
        /// <param name="collectionName"></param>
        /// <returns></returns>
        public static MongoCollection<T> GetCollectionAs<T>(string applicationName, string connectionString, string databaseName,
                                                            string collectionName) {
            var server = MongoServer.Create(connectionString);
            var database = server.GetDatabase(databaseName, SafeMode.True);
            return database.GetCollection<T>(applicationName + "." + collectionName);
        }
       
        public static void InitializeCollections(string applicationName, string connectionString, string databaseName) {
            // Add users collection.
            var users = GetCollectionAs<MongoMembershipUser>(applicationName, connectionString, databaseName, "users");
            if (!users.Exists()) {
                users.ResetIndexCache();
                users.EnsureIndex(IndexKeys.Ascending("UserName"), IndexOptions.SetUnique(true));
                users.EnsureIndex(IndexKeys.Ascending("Email"));
            }

            // Add roles collection.
            var roles = GetCollectionAs<MongoRole>(applicationName, connectionString, databaseName, "roles");
            if (!roles.Exists()) {
                roles.ResetIndexCache();
                roles.EnsureIndex(IndexKeys.Ascending("Name"), IndexOptions.SetUnique(true));
            }
        }

    }
}
