namespace CiviBotti.DataModels;

using System;
using System.Collections.Generic;
using Services;

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

    public void InsertDatabase(IDatabase db)
    {
        var sql = $"INSERT INTO players (gameid, steamid, turnorder, nexteta) values ({_gameId}, {SteamId}, {TurnOrder}, '{NextEta}')";

        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    public void UpdateDatabase(IDatabase db)
    {
        var sql = $"UPDATE players SET turnorder = {TurnOrder}, nexteta = '{NextEta}' WHERE gameid = {_gameId} AND steamId = {SteamId}";

        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    public override string ToString() => $"Player: {Name}";

    public static PlayerData? GetBySteamAndGameId(IDatabase db, string gameId, long currentPlayerSteamId) {
        var sql = $"SELECT * FROM players WHERE steamid = {currentPlayerSteamId} AND gameid = {gameId}";
        var reader = db.ExecuteReader(sql);

        return !reader.Read() ? null : new PlayerData(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetDateTime(3));
    }
    public static List<PlayerData> GetByGameId(IDatabase db, string gameId) {
        var sql = $"SELECT * FROM players WHERE gameid = {gameId}";
        var reader = db.ExecuteReader(sql);
        var players = new List<PlayerData>();

        while (reader.Read()) {
            var player = new PlayerData(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetDateTime(3));
            players.Add(player);
            
            var user = UserData.GetBySteamId(db, player.SteamId);
            if (user != null) {
                player.User = user;
            }
        }

        return players;
    }
}