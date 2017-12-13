using System;
using System.Data.Entity.ModelConfiguration.Conventions;

namespace CiviBotti
{
    public class PlayerData
    {
        public long GameId;
        public string SteamId;
        public UserData User;
        public int TurnOrder;
        public DateTime NextEta;

        public string SteamName = "";
        public string TgName = "";

        public string Name => ((TgName?.Length > 0) ? TgName : ((SteamName?.Length > 0) ? SteamName : SteamId));
        public string Nametag => ((TgName?.Length > 0) ? "@" + TgName : ((SteamName?.Length > 0) ? SteamName : SteamId));

        public bool InsertDatabase()
        {
            var sql = $"INSERT INTO players (gameid, steamid, turnorder, nexteta) values ({GameId}, {SteamId}, {TurnOrder}, '{NextEta}')";

            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }

        public bool UpdateDatabase()
        {
            var sql = $"UPDATE players SET turnorder = {TurnOrder}, nexteta = '{NextEta}' WHERE gameid = {GameId} AND steamId = {SteamId}";

            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }

        public bool GetNextEta()
        {
            var sql = $"SELECT nexteta FROM players WHERE gameid = {GameId} AND steamid = {SteamId}";
            var reader = Program.Database.ExecuteReader(sql);
            if (!reader.HasRows) return false;
            reader.Read();
            
            return DateTime.TryParse(reader.GetString(0), out NextEta);
        }

        public static bool CheckDatabase(long gameId, string steamId)
        {
            var sql = $"SELECT * FROM players WHERE gameid = {gameId} AND steamid = {steamId}";
            var reader = Program.Database.ExecuteReader(sql);
            var result = reader.HasRows;
            reader.Close();
            return result;
        }

        public override string ToString() => $"Player: {Name}";
    }
}