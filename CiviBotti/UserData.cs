using System;

namespace CiviBotti {
    public class UserData {
        public long ID;
        public string steamID;
        public string authKey;

        public bool InsertDatabase(bool open) {
            bool result = false;

            string sql = $"INSERT INTO users (id, steamid, authkey) values ({ID}, '{steamID}', '{authKey}')";
            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);

            if (rows == 1) {
                result = true;
            }
                
            return result;
        }

        public static bool CheckDatabase(long id) {
            string sql = $"SELECT * FROM users WHERE id = {id}";
            DatabaseReader reader = Program.database.ExecuteReader(sql);
            bool result = reader.HasRows;
            reader.Close();
            return result;
        }

        public static UserData Get(long id) {
            string sql = $"SELECT * FROM users WHERE id = {id}";
            Console.WriteLine(sql);
            DatabaseReader reader = Program.database.ExecuteReader(sql);


            UserData user = null;
            if (reader.Read()) {
                user = new UserData();
                user.ID = reader.GetInt64(0);
                user.steamID = reader.GetString(1);
                user.authKey = reader.GetString(2);
            }

            reader.Close();
            return user;
        }
        
        public static UserData GetBySteamID(string steamid) {
            string sql = $"SELECT * FROM users WHERE steamid = '{steamid}'";
            DatabaseReader reader = Program.database.ExecuteReader(sql);


            UserData user = null;
            if (reader.Read()) {
                user = new UserData();

                user.ID = reader.GetInt64(0);
                user.steamID = reader.GetString(1);
                user.authKey = reader.GetString(2);
            }

            reader.Close();
            return user;
        }

        public override string ToString() {
            return base.ToString() +" ("+ steamID + ")";
        }
    }
}