namespace CiviBotti.DataModels;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Exceptions;
using Services;

public class GameData(
    long gameId,
    UserData owner,
    string name,
    PlayerData currentPlayer,
    bool turntimerNotified,
    bool isOver)
{
    public long GameId { get; } = gameId;
    public UserData Owner { get; private set; } = owner;

    public List<PlayerData> Players { get; } = [];
    public string Name { get; } = name;
    public List<long> Chats { get; } = [];
    public PlayerData CurrentPlayer { get; set; } = currentPlayer;
    public DateTime TurnStarted { get; set;}
    public bool TurntimerNotified { get; set;} = turntimerNotified;
    public bool DailyNotified { get; set;}
    public bool EnableDailyNotified { get; } = true;

    public bool IsOver { get; set; } = isOver;

    public string TurnId { get; set; } = "";
    

    public void UpdateOwner(IDatabase db, UserData nextOwnerUser) {
        Owner = nextOwnerUser;
        var sql = $"UPDATE games SET ownerid = {Owner.SteamId} WHERE gameid = {GameId}";

        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }
    private void InsertDatabase(IDatabase db) {
        var sql = $"INSERT INTO games (gameid, ownerid, name, currentp, notified, turnid, enableDailyNotified, dailyNotified) values ({GameId}, {Owner.Id}, '{Name}', {CurrentPlayer.SteamId}, '{(TurntimerNotified ? 1 : 0)}', '{TurnId}', '{(EnableDailyNotified ? 1 : 0)}', '{(DailyNotified ? 1 : 0)}')";
        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }


    public void UpdateCurrent(IDatabase db) {
        var sql = $"UPDATE games SET currentp = {CurrentPlayer.SteamId}, notified = {(TurntimerNotified ? 1 : 0)}, turnid = '{TurnId}', enableDailyNotified = {(EnableDailyNotified ? 1 : 0)}, dailyNotified = {(DailyNotified ? 1 : 0)} WHERE gameid = {GameId}";

        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    private void InsertDatabasePlayers(IDatabase db) {
        foreach (var player in Players) {
            player.InsertDatabase(db);
        }
    }

    private void InsertChats(IDatabase db) {
        foreach (var sql in Chats.Select(chat => $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chat})")) {
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }
    }

    public void InsertChat(IDatabase db, long chatId) {
        var sql = $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chatId})";
        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    public void InsertFull(IDatabase db) {
        InsertDatabase(db);
        InsertDatabasePlayers(db);
        InsertChats(db);
    }

    public static IEnumerable<GameData> GetAllGames(IDatabase db) {
        const string sql = "SELECT * FROM games G LEFT JOIN players P ON G.gameid = P.gameid LEFT JOIN users U ON P.steamid = U.steamid;";
        var reader = db.ExecuteReader(sql);

        var gameDatas = new List<GameData>();

        List<PlayerData> players = new();

        if (!reader.Read()) return gameDatas;
        string currentPlayerSteamId;
        long ownerId;
        bool moreData;
        do {
            var gId = reader.GetInt64(0);
            ownerId = reader.GetInt64(1);
            var gameName = reader.GetString(2);
            currentPlayerSteamId = reader.GetString(3);
            var turnTimeNotified = reader.GetBoolean(4);

            var over = reader.GetBoolean(8);
            var playerSteamId = reader.GetString(10);
            var playerTurnOrder = reader.GetInt32(11);
            
            var dateString = reader.GetString(12);
            if (!DateTime.TryParse(dateString, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.AssumeLocal, out var nextEta)) {
                nextEta = DateTime.MinValue;
            }
            var playerData = new PlayerData(gId, playerSteamId, playerTurnOrder, nextEta);
            var userId = reader.IsDBNull(13) ? 0 : reader.GetInt64(13);
            if (userId > 0) {
                playerData.User = new UserData(userId, reader.GetString(14), reader.GetString(15));
            }
            players.Add(playerData);

            moreData = reader.Read();
            
            if (moreData && gId == reader.GetInt64(0)) {
                continue;
            }

            var cp = players.Find(p => p.SteamId == currentPlayerSteamId);
            if (cp == null) {
                throw new MalformedDatabaseException("Current player not found");
            }

            var o = players.Select(p => p.User).FirstOrDefault(p => p?.Id == ownerId);
            if (o == null) {
                throw new MalformedDatabaseException("Owner not found");
            }

            var game = new GameData(gId, o, gameName, cp, turnTimeNotified, over);
            game.Players.Clear();
            game.Players.AddRange(players);
            gameDatas.Add(game);
            players.Clear();
        } while (moreData);
        reader.Close();

        GetAllChats(db, gameDatas);

        return gameDatas;
    }

    private static void GetAllChats(IDatabase db, List<GameData> gameDatas) {
        foreach (var gameData in gameDatas) {
            var sql2 = $"SELECT chatid FROM gamechats WHERE gameid = {gameData.GameId}";
            var reader2 = db.ExecuteReader(sql2);
            while (reader2.Read()) {
                gameData.Chats.Add(reader2.GetInt64(0));
            }

            reader2.Close();
        }
    }

    public void RemoveChat(IDatabase db, long id) {
        var sql = $"DELETE FROM gamechats WHERE gameid = {GameId} AND chatid = {id}";
        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    public override string ToString() => $"{Name} ({GameId})";
}