using System;

namespace CiviBotti
{
    public class PlayerData
    {
        private readonly long _gameId;
        public readonly string SteamId;
        public UserData? User { get; set; }
        public readonly int TurnOrder;
        public DateTime NextEta { get; set; }

        public string SteamName  { get; set; } = "";
        public string TgName  { get; set; } = "";

        public PlayerData(long gameId, string steamId, int turnOrder, DateTime nextEta) {
            _gameId = gameId;
            SteamId = steamId;
            TurnOrder = turnOrder;
            NextEta = nextEta;
        }

        public string Name {
            get {
                if (TgName.Length > 0) {
                    return TgName;
                }

                return SteamName.Length > 0 ? SteamName : SteamId;
            }
        }

        public string NameTag {
            get {
                if (TgName.Length > 0) {
                    return "@" + TgName;
                }

                return SteamName.Length > 0 ? SteamName : SteamId;
            }
        }

        public void InsertDatabase()
        {
            var sql = $"INSERT INTO players (gameid, steamid, turnorder, nexteta) values ({_gameId}, {SteamId}, {TurnOrder}, '{NextEta}')";

            Console.WriteLine(sql);
            Program.Database.ExecuteNonQuery(sql);
        }

        public void UpdateDatabase()
        {
            var sql = $"UPDATE players SET turnorder = {TurnOrder}, nexteta = '{NextEta}' WHERE gameid = {_gameId} AND steamId = {SteamId}";

            Console.WriteLine(sql);
            Program.Database.ExecuteNonQuery(sql);
        }

        public override string ToString() => $"Player: {Name}";
    }
}