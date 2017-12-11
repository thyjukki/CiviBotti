using System;
using System.Collections.Generic;

namespace CiviBotti {
    public class GameData {
        public long GameId;
        public UserData Owner;
        private long _ownerRaw;
        public List<long> Chats;
        public PlayerData CurrentPlayer;
        public DateTime TurnStarted;
        public bool TurntimerNotified;
        private string _currentPlayerRaw;

        public List<PlayerData> Players;
        public string Name;

        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public bool InsertDatabase() {
            var sql = $"INSERT INTO games (gameid, ownerid, name, currentp, started, notified) values ({GameId}, {Owner.Id}, '{Name}', {CurrentPlayer.SteamId}, '{TurnStarted}', '{TurntimerNotified}')";
            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }


        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public bool UpdateCurrent() {
            var sql = $"UPDATE games SET currentp = {CurrentPlayer.SteamId}, started = {CurrentPlayer.SteamId}, notified = {TurntimerNotified} WHERE gameid = {GameId}";

            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            return rows == 1;
        }

        public void InsertDatabasePlayers() {
            if (Players == null) {
                return;
            }
            foreach (var player in Players) {
                player.InsertDatabase();
            }
        }

        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public void InsertChats() {
            if (Chats == null) {
                return;
            }

            foreach (var chat in Chats) {
                var sql = $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chat})";
                Console.WriteLine(sql);
                Program.Database.ExecuteNonQuery(sql);
            }
        }

        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public void InsertChat(long chatId) {
            if (Chats == null) {
                return;
            }
     
            var sql = $"INSERT INTO gamechats (gameid, chatid) values ({GameId}, {chatId})";
            Console.WriteLine(sql);
            Program.Database.ExecuteNonQuery(sql);
        }

        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        public void InsertFull() {
            InsertDatabase();
            InsertDatabasePlayers();
            InsertChats();
        }

        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public static bool CheckDatabase(long gameId) {
            var sql = $"SELECT * FROM games WHERE gameid = {gameId}";
            var reader = Program.Database.ExecuteReader(sql);
            var result = reader.HasRows;
            reader.Close();
            return result;
        }

        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public static List<GameData> GetAllGames() {
            const string sql = "SELECT * FROM games";
            var reader = Program.Database.ExecuteReader(sql);

            var collection = new List<GameData>();
            while (reader.Read()) {
                var game = new GameData
                {
                    GameId = reader.GetInt64(0),
                    _ownerRaw = reader.GetInt64(1),
                    Name = reader.GetString(2),
                    _currentPlayerRaw = reader.GetString(3),
                    Players = new List<PlayerData>(),
                    Chats = new List<long>(),
                    TurntimerNotified = reader.GetBit(4)
                };
                DateTime.TryParse(reader.GetString(5), out game.TurnStarted);



                collection.Add(game);
            }
            reader.Close();

            foreach (var game in collection) {
                game.Owner = UserData.Get(game._ownerRaw);


                var sql2 = $"SELECT * FROM players WHERE gameid = {game.GameId}";
                var reader2 = Program.Database.ExecuteReader(sql2);
                while (reader2.Read()) {
                    var player = new PlayerData
                    {
                        GameId = reader2.GetInt64(0),
                        SteamId = reader2.GetString(1),
                        TurnOrder = reader2.GetInt32(2)
                    };
                    DateTime.TryParse(reader2.GetString(3), out player.NextEta);

                    if (game._currentPlayerRaw == player.SteamId) {
                        game.CurrentPlayer = player;
                    }
                    game.Players.Add(player);
                }
                reader2.Close();

                foreach (var player in game.Players) {
                    player.User = UserData.GetBySteamId(player.SteamId);
                }


                var sql3 = $"SELECT chatid FROM gamechats WHERE gameid = {game.GameId}";
                var reader3 = Program.Database.ExecuteReader(sql3);
                while (reader3.Read()) {

                    game.Chats.Add(reader3.GetInt64(0));
                }
                reader3.Close();
            }

            return collection;
        }

        public void RemoveChat(long id) {
            var sql = $"DELETE FROM gamechats WHERE gameid = {GameId} AND chatid = {id}";
            Console.WriteLine(sql);
            Program.Database.ExecuteNonQuery(sql);
        }
    }
}