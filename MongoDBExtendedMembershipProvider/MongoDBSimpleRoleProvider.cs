
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Configuration.Provider;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Security;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace MongoDBExtendedMembershipProvider
{
    public partial class MongoDBSimpleRoleProvider : RoleProvider
    {
        #region Constructor(s)
        private MongoDatabase mongoDB;
        private readonly string WEBPAGESROLE = "WebpagesRole";

        public MongoDBSimpleRoleProvider()
        {
        }

        #endregion

        #region Initialize
        public override void Initialize(string name, NameValueCollection config)
        {
            if (config == null)
                throw new ArgumentNullException("config");
            if (string.IsNullOrEmpty(name))
                name = "ExtendedAdapterRoleProvider";
            if (string.IsNullOrEmpty(config["description"]))
            {
                config.Remove("description");
                config.Add("description", "Adapter Extended Role Provider");
            }
            base.Initialize(name, config);

            ApplicationName = GetValueOrDefault(config, "applicationName", o => o.ToString(), "MySampleApp");


            // MongoDB setup
            this.ConnectionStringName = GetValueOrDefault(config, "connectionStringName", o => o.ToString(), string.Empty);
            MongoConnectionStringBuilder conString = new MongoConnectionStringBuilder(ConfigurationManager.ConnectionStrings[ConnectionStringName].ConnectionString);
            MongoServer server = new MongoClient(conString.ToString()).GetServer();
            this.mongoDB = server.GetDatabase(conString.DatabaseName);
            // set id autoincrement generator
            BsonClassMap.RegisterClassMap<WebpagesRole>(cm =>
            {
                cm.AutoMap();
                cm.IdMemberMap.SetIdGenerator(new IntIdGenerator());
            });
            var rolesMongoCollection = mongoDB.GetCollection("WebpagesRole");
            var usersInRolesMongoCollection = mongoDB.GetCollection("WebpagesUsersInRole");
            rolesMongoCollection.EnsureIndex("RoleName");
            //this.rolesMongoCollection.EnsureIndex("ApplicationName", "Role");
            //this.usersInRolesMongoCollection.EnsureIndex("ApplicationName", "Role");
            //this.usersInRolesMongoCollection.EnsureIndex("ApplicationName", "UserName");
            //this.usersInRolesMongoCollection.EnsureIndex("ApplicationName", "Role", "UserName");

            config.Remove("name");
            config.Remove("description");
            config.Remove("applicationName");
            config.Remove("connectionStringName");

            if (config.Count <= 0)
                return;
            var key = config.GetKey(0);
            if (string.IsNullOrEmpty(key))
                return;

            throw new ProviderException(string.Format(CultureInfo.CurrentCulture,
                                                      "The role provider does not recognize the configuration attribute {0}.",
                                                      key));
        }

        public string ConnectionStringName { get; set; }
        #endregion

        #region Abstract Property Overrides
        public override string ApplicationName { get; set; }
        #endregion

        #region Abstract Method Overrides
        public override void AddUsersToRoles(string[] usernames, string[] roleNames)
        {
                var users = GetUsers(usernames);
                var roles = GetRoles(roleNames);

                var userIds = users.Select(u => u.UserId).ToArray();
                var roleIds = roles.Select(r => r.RoleId).ToArray();


                
            foreach (var role in roles)
            {
                var query = Query.EQ("RoleId", role.RoleId);
                query = Query.And(query, Query.In("UserId", new BsonArray(userIds)));
                var alreadyExist =
                    this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Find(query);
                if (alreadyExist.Count() > 0)
                {
                    throw new InvalidOperationException(string.Format("User with id = {0} already exists in role {1}", alreadyExist.First().UserId, role.RoleName));
                }

                var roleUser = users.Select(u => new WebpagesUsersInRole() {RoleId = role.RoleId, UserId = u.UserId});
                this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").InsertBatch(roleUser, WriteConcern.Acknowledged);
            }
        }

        public override void CreateRole(string roleName)
        {
            var role = new WebpagesRole { RoleName = roleName };
            if (GetRoles(new[] { roleName }).Count() > 0)
                throw new ProviderException(string.Format("Role {0} already exists!", roleName));
            //role.RoleId = (int)this.mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).Count();
            this.mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).Insert(role, WriteConcern.Acknowledged);
        }

        public override bool DeleteRole(string roleName, bool throwOnPopulatedRole)
        {
            var role = GetRoles(new[] { roleName }).FirstOrDefault();
            if (role == null)
                throw new ProviderException(string.Format("Role {0} does not exist!", roleName));

            if (throwOnPopulatedRole)
            {
                var query = Query.EQ("RoleId", role.RoleId);
                var usersInRole = this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Find(query);
                if (usersInRole.Count() > 0)
                {
                    throw new ProviderException(string.Format("Role {0} is not empty!", roleName));
                }
            }
            else
            {
                var query = Query.EQ("RoleId", role.RoleId);
                this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Remove(query, WriteConcern.Acknowledged);
            }
            return this.mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).Remove(
                Query.EQ("RoleId", role.RoleId), WriteConcern.Acknowledged).Ok;
        }

        public override string[] FindUsersInRole(string roleName, string usernameToMatch)
        {
            var role = GetRoles(new[] { roleName }).FirstOrDefault();
            var users = this.mongoDB.GetCollection<UserProfile>("UserProfile").Find(
                Query.Matches("UserName", usernameToMatch));
            if (users == null)
                throw new ProviderException(string.Format("User {0} does not exist!", usernameToMatch));
            if (role == null)
                throw new ProviderException(string.Format("Role {0} does not exist!", roleName));

            var query = Query.In("UserId", new BsonArray(users.Select(u => u.UserId).ToArray()));
            query = Query.And(query, Query.EQ("RoleId", role.RoleId));
            var usersInRole = this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Find(query);

            if (usersInRole != null)
            {
                query = Query.In("UserId", new BsonArray(usersInRole.Select(u => u.UserId).ToArray()));
                var foundUsers = this.mongoDB.GetCollection<UserProfile>("UserProfile").Find(query);

                if (foundUsers != null)
                {
                    return foundUsers.Select(u => u.UserName).OrderBy(u => u).ToArray();
                }
            }

            return new string[] { };


        }

        public override string[] GetAllRoles()
        {
            var roles = this.mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).FindAll();

            if (roles.Count() > 0)
            {
                return roles.Select(r => r.RoleName).OrderBy(u => u).ToArray();
            }
            return new string[] { };
        }


        public override string[] GetRolesForUser(string username)
        {
            var user = GetUsers(new[] { username }).FirstOrDefault();

            if (user != null)
            {
                var query = Query.EQ("UserId", user.UserId);
                var roleUser = this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Find(query);

                if (roleUser != null)
                {
                    query = Query.In("RoleId", new BsonArray(roleUser.Select(p => p.RoleId)));
                    var roles = this.mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).Find(query);
                    return roles.Select(r => r.RoleName).OrderBy(u => u).ToArray();
                }
            }
            return new string[] { };
        }

        public override string[] GetUsersInRole(string roleName)
        {
            var role = GetRoles(new[] { roleName }).FirstOrDefault();

            if (role != null)
            {
                var query = Query.EQ("RoleId", role.RoleId);
                var roleUser = this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Find(query);

                if (roleUser != null)
                {
                    query = Query.In("UserId", new BsonArray(roleUser.Select(p => p.UserId)));
                    var users = this.mongoDB.GetCollection<UserProfile>("UserProfile").Find(query);
                    return users.Select(u => u.UserName).OrderBy(u => u).ToArray();
                }
            }
            return new string[] { };
        }

        public override bool IsUserInRole(string username, string roleName)
        {
            {
                var user = GetUsers(new[] { username }).FirstOrDefault();
                var role = GetRoles(new[] { roleName }).FirstOrDefault();

                if (user == null)
                    throw new ProviderException(string.Format("User {0} does not exist!", username));
                if (role == null)
                    throw new ProviderException(string.Format("Role {0} does not exist!", roleName));

                var query = Query.EQ("UserId", user.UserId);
                query = Query.And(query, Query.EQ("RoleId", role.RoleId));
                return this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").FindOne(query) != null;
            }
        }

        public override void RemoveUsersFromRoles(string[] usernames, string[] roleNames)
        {
            var users = GetUsers(usernames);
            var roles = GetRoles(roleNames);

            var roleIds = roles.Select(r => r.RoleId).ToArray();
            var userIds = users.Select(u => u.UserId).ToArray();

            var query = Query.In("UserId", new BsonArray(userIds));
            query = Query.And(query, Query.In("RoleId", new BsonArray(roleIds)));
            this.mongoDB.GetCollection<WebpagesUsersInRole>("WebpagesUsersInRole").Remove(query, WriteConcern.Acknowledged);

        }

        public override bool RoleExists(string roleName)
        {
            return GetRoles(new string[] { roleName }).Count() > 0;
        }
        #endregion

        #region Helper Methods
        private static T GetValueOrDefault<T>(NameValueCollection nvc, string key, Func<object, T> converter, T defaultIfNull)
        {
            var val = nvc[key];

            if (val == null)
                return defaultIfNull;

            try
            {
                return converter(val);
            }
            catch
            {
                return defaultIfNull;
            }
        }

        private IEnumerable<UserProfile> GetUsers(string[] usernames)
        {
            var users = this.mongoDB.GetCollection<UserProfile>("UserProfile").Find(Query.In("UserName", new BsonArray(usernames)));
            return users;
        }

        private IEnumerable<WebpagesRole> GetRoles(string[] rolenames)
        {
            var roles =
                mongoDB.GetCollection<WebpagesRole>(WEBPAGESROLE).Find(Query.In("RoleName", new BsonArray(rolenames)));

            return roles;
        }
        #endregion
    }
}
