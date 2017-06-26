using System;

namespace CiviBotti {
    public class PlayerData {
        public long gameID;
        public string steamID;
        public UserData user;
        public int turnOrder;

        public bool InsertDatabase() {
            string sql = $"INSERT INTO players (gameid, steamid, turnorder) values ({gameID}, {steamID}, {turnOrder})";
            
            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);

            if (rows == 1) {
                return true;
            }

            return false;
        }

        public bool UpdateDatabase() {
            string sql = $"UPDATE players SET turnorder = {turnOrder} WHERE gameid = {gameID} AND steamId = {steamID}";

            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);

            if (rows == 1) {
                return true;
            }

            return false;
        }

        public static bool CheckDatabase(long gameID, string steamID) {
            string sql = $"SELECT * FROM players WHERE gameid = {gameID} AND steamid = {steamID}";
            DatabaseReader reader = Program.database.ExecuteReader(sql);
            bool result = reader.HasRows;
            reader.Close();
            return result;
        }
    }
}