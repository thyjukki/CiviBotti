using System;

namespace CiviBotti {
    public class PlayerData {
        public long GameId;
        public string SteamId;
        public UserData User;
        public int TurnOrder;

        public bool InsertDatabase() {
            var sql = $"INSERT INTO players (gameid, steamid, turnorder) values ({GameId}, {SteamId}, {TurnOrder})";
            
            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }

        public bool UpdateDatabase() {
            var sql = $"UPDATE players SET turnorder = {TurnOrder} WHERE gameid = {GameId} AND steamId = {SteamId}";

            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }

        public static bool CheckDatabase(long gameId, string steamId) {
            var sql = $"SELECT * FROM players WHERE gameid = {gameId} AND steamid = {steamId}";
            var reader = Program.Database.ExecuteReader(sql);
            var result = reader.HasRows;
            reader.Close();
            return result;
        }
    }
}