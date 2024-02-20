using System;
using System.Collections.Generic;

namespace CiviBotti {
    public class UserData
    {
        private static readonly List<UserData> Users = new List<UserData>();

        public long Id { get; private set; }
        public string SteamId { get; private set; } = "";
        public string AuthKey { get; private set; } = "";

        public List<SubData> Subs { get; private set; } = new ();

        private UserData()
        {
            Users.Add(this);
        }

        public bool InsertDatabase(Database db) {
            var result = false;

            var sql = $"INSERT INTO users (id, steamid, authkey) values ({Id}, '{SteamId}', '{AuthKey}')";
            Console.WriteLine(sql);
            var rows = db.ExecuteNonQuery(sql);

            if (rows == 1) {
                result = true;
            }
                
            return result;
        }

        public static bool CheckDatabase(Database db, long id) {
            var sql = $"SELECT * FROM users WHERE id = {id}";
            var reader = db.ExecuteReader(sql);
            var result = reader.HasRows;
            reader.Close();
            return result;
        }

        public static UserData? Get(Database db, long id) {
            var check = Users.Find(cachedUser => cachedUser.Id == id);
            if (check != null) return check;

            var sql = $"SELECT * FROM users WHERE id = {id}";
            Console.WriteLine(sql);
            var reader = db.ExecuteReader(sql);


            UserData user;
            if (reader.Read()) {
                user = new UserData {
                    Id = reader.GetInt64(0),
                    SteamId = reader.GetString(1),
                    AuthKey = reader.GetString(2)
                };
            }
            else {
                return null;
            }

            reader.Close();
            user.Subs = SubData.Get(db, user.Id);
            return user;
        }
        
        public static UserData? GetBySteamId(Database db, string steamId) {
            var sql = $"SELECT * FROM users WHERE steamid = '{steamId}'";
            var reader = db.ExecuteReader(sql);


            UserData? user = null;
            if (reader.Read()) {
                var check = Users.Find(_ => _.Id == reader.GetInt64(0));
                if (check != null) {
                    user = check;
                } else {
                    user = new UserData {
                        Id = reader.GetInt64(0),
                        SteamId = reader.GetString(1),
                        AuthKey = reader.GetString(2)
                    };
                }
            }
            
            reader.Close();
            if (user != null)
            {
                user.Subs = SubData.Get(db, user.Id);
            }
            return user;
        }

        public static UserData NewUser(long fromId, string steamId, string authKey) {
            return new UserData { Id = fromId, SteamId = steamId, AuthKey = authKey };
        }
    }
}