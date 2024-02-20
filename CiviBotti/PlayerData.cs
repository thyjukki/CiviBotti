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
        public string TgName { get; set; } = "";

        public PlayerData(long gameId, string steamId, int turnOrder, DateTime nextEta) {
            _gameId = gameId;
            SteamId = steamId;
            TurnOrder = turnOrder;
            NextEta = nextEta;
        }

        public string Name {
            get {
                if (!string.IsNullOrEmpty(TgName)) {
                    return TgName;
                }

                return SteamName.Length > 0 ? SteamName : SteamId;
            }
        }

        public string NameTag {
            get {
                if (!string.IsNullOrEmpty(TgName)) {
                    return "@" + TgName;
                }

                return SteamName.Length > 0 ? SteamName : SteamId;
            }
        }

        public void InsertDatabase(Database db)
        {
            var sql = $"INSERT INTO players (gameid, steamid, turnorder, nexteta) values ({_gameId}, {SteamId}, {TurnOrder}, '{NextEta}')";

            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        public void UpdateDatabase(Database db)
        {
            var sql = $"UPDATE players SET turnorder = {TurnOrder}, nexteta = '{NextEta}' WHERE gameid = {_gameId} AND steamId = {SteamId}";

            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        public override string ToString() => $"Player: {Name}";
    }
}