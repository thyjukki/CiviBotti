using System;

namespace CiviBotti {
    public class UserData {
        public long Id;
        public string SteamId;
        public string AuthKey;

        public bool InsertDatabase(bool open) {
            var result = false;

            var sql = $"INSERT INTO users (id, steamid, authkey) values ({Id}, '{SteamId}', '{AuthKey}')";
            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            if (rows == 1) {
                result = true;
            }
                
            return result;
        }

        public static bool CheckDatabase(long id) {
            var sql = $"SELECT * FROM users WHERE id = {id}";
            var reader = Program.Database.ExecuteReader(sql);
            var result = reader.HasRows;
            reader.Close();
            return result;
        }

        public static UserData Get(long id) {
            var sql = $"SELECT * FROM users WHERE id = {id}";
            Console.WriteLine(sql);
            var reader = Program.Database.ExecuteReader(sql);


            UserData user = null;
            if (reader.Read()) {
                user = new UserData
                {
                    Id = reader.GetInt64(0),
                    SteamId = reader.GetString(1),
                    AuthKey = reader.GetString(2)
                };
            }

            reader.Close();
            return user;
        }
        
        public static UserData GetBySteamId(string steamid) {
            var sql = $"SELECT * FROM users WHERE steamid = '{steamid}'";
            var reader = Program.Database.ExecuteReader(sql);


            UserData user = null;
            if (reader.Read()) {
                user = new UserData
                {
                    Id = reader.GetInt64(0),
                    SteamId = reader.GetString(1),
                    AuthKey = reader.GetString(2)
                };

            }

            reader.Close();
            return user;
        }

        public override string ToString() => $"User: {SteamId}";
    }
}