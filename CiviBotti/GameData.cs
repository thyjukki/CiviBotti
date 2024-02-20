using System;
using System.Collections.Generic;

namespace CiviBotti {
    using System.Globalization;
    using System.Linq;

    public class GameData {
        public long GameId { get; init;}
        public UserData? Owner { get; set; }
        private long _ownerRaw;
        public List<long> Chats { get; } = new();
        public PlayerData CurrentPlayer { get; set;}
        public DateTime TurnStarted { get; set;}
        public bool TurntimerNotified { get; set;}
        public bool DailyNotified { get; set;}
        public bool EnableDailyNotified { get; private init;}
        private string _currentPlayerRaw = "";

        public List<PlayerData> Players { get; set;} = new();
        public string Name { get; set; } = "";
        public string TurnId { get; set; } = "";

        private void InsertDatabase(Database db) {
            var sql = $"INSERT INTO games (gameid, ownerid, name, currentp, notified, turnid, enableDailyNotified, dailyNotified) values ({GameId}, {Owner.Id}, '{Name}', {CurrentPlayer.SteamId}, '{(TurntimerNotified ? 1 : 0)}', '{TurnId}', '{(EnableDailyNotified ? 1 : 0)}', '{(DailyNotified ? 1 : 0)}')";
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }


        public void UpdateCurrent(Database db) {
            var sql = $"UPDATE games SET currentp = {CurrentPlayer.SteamId}, notified = {(TurntimerNotified ? 1 : 0)}, turnid = '{TurnId}', enableDailyNotified = {(EnableDailyNotified ? 1 : 0)}, dailyNotified = {(DailyNotified ? 1 : 0)} WHERE gameid = {GameId}";

            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        private void InsertDatabasePlayers(Database db) {
            foreach (var player in Players) {
                player.InsertDatabase(db);
            }
        }

        private void InsertChats(Database db) {
            foreach (var sql in Chats.Select(chat => $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chat})")) {
                Console.WriteLine(sql);
                db.ExecuteNonQuery(sql);
            }
        }

        public void InsertChat(Database db, long chatId) {
            var sql = $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chatId})";
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        public void InsertFull(Database db) {
            InsertDatabase(db);
            InsertDatabasePlayers(db);
            InsertChats(db);
        }

        public static List<GameData> GetAllGames(Database db) {
            const string sql = "SELECT * FROM games";
            var reader = db.ExecuteReader(sql);

            var collection = new List<GameData>();
            while (reader.Read()) {
                var game = new GameData
                {
                    GameId = reader.GetInt64(0),
                    _ownerRaw = reader.GetInt64(1),
                    Name = reader.GetString(2),
                    _currentPlayerRaw = reader.GetString(3),
                    Players = new List<PlayerData>(),
                    TurntimerNotified = reader.GetBoolean(4),
                    TurnId = reader.GetString(5),
                    EnableDailyNotified = reader.GetBoolean(6),
                    DailyNotified = reader.GetBoolean(7),
                };



                collection.Add(game);
            }
            reader.Close();

            

            return collection;
        }

        public void GetGameData(Database db) {


            var sql2 = $"SELECT * FROM players WHERE gameid = {GameId}";
            var reader2 = db.ExecuteReader(sql2);
            while (reader2.Read()) {
                var dateString = reader2.GetString(3);
                DateTime.TryParse(dateString, DateTimeFormatInfo.CurrentInfo, DateTimeStyles.AssumeLocal, out var nextEta);
                
                var player = new PlayerData(reader2.GetInt64(0), reader2.GetString(1),
                    reader2.GetInt32(2), nextEta);

                if (_currentPlayerRaw == player.SteamId) {
                    CurrentPlayer = player;
                }
                Players.Add(player);
            }
            reader2.Close();

            foreach (var player in Players) {
                player.User = UserData.GetBySteamId(db, player.SteamId);
                if (player.User?.Id == _ownerRaw) {
                    Owner = player.User;
                }
            }


            var sql3 = $"SELECT chatid FROM gamechats WHERE gameid = {GameId}";
            var reader3 = db.ExecuteReader(sql3);
            while (reader3.Read()) {
                Chats.Add(reader3.GetInt64(0));
            }
            reader3.Close();
        }

        public void RemoveChat(Database db, long id) {
            var sql = $"DELETE FROM gamechats WHERE gameid = {GameId} AND chatid = {id}";
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        public override string ToString() => $"{Name} ({GameId})";
    }
}