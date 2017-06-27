using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Telegram.Bot.Types;

namespace CiviBotti {
    public class GameData {
        public long gameID;
        public UserData owner;
        private long ownerRaw;
        public List<long> chats;
        public PlayerData currentPlayer;
        private string currentPlayerRaw;

        public List<PlayerData> players;
        public string name;

        public bool InsertDatabase() {
            string sql = $"INSERT INTO games (gameid, ownerid, name, currentp) values ({gameID}, {owner.ID}, '{name}', {currentPlayer.steamID})";
            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);

            if (rows == 1) {
                return true;
            }

            return false;
        }


        public bool UpdateCurrent() {
            string sql = $"UPDATE games SET currentp = {currentPlayer.steamID} WHERE gameid = {gameID}";

            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);

            if (rows == 1) {
                return true;
            }

            return false;
        }

        public void InsertDatabasePlayers() {
            if (players == null) {
                return;
            }
            foreach (var player in players) {
                player.InsertDatabase();
            }
        }

        public void InsertChats() {
            if (chats == null) {
                return;
            }

            foreach (var chat in chats) {
                string sql = $"INSERT INTO gamechats (gameid, chatid) values ({gameID}, {chat})";
                Console.WriteLine(sql);
                Program.database.ExecuteNonQuery(sql);
            }
        }

        public void InsertChat(long chatID) {
            if (chats == null) {
                return;
            }
     
            string sql = $"INSERT INTO gamechats (gameid, chatid) values ({gameID}, {chatID})";
            Console.WriteLine(sql);
            Program.database.ExecuteNonQuery(sql);
        }

        public void InsertFull() {
            InsertDatabase();
            InsertDatabasePlayers();
            InsertChats();
        }

        public static bool CheckDatabase(long gameID) {
            string sql = $"SELECT * FROM games WHERE gameid = {gameID}";
            DatabaseReader reader = Program.database.ExecuteReader(sql);
            bool result = reader.HasRows;
            reader.Close();
            return result;
        }

        public static List<GameData> GetAllGames() {
            string sql = $"SELECT * FROM games";
            DatabaseReader reader = Program.database.ExecuteReader(sql);

            var collection = new List<GameData>();
            while (reader.Read()) {
                var game = new GameData();

                game.gameID = reader.GetInt64(0);
                game.name = reader.GetString(2);
                game.currentPlayerRaw = reader.GetString(3);
                game.ownerRaw = reader.GetInt64(1);
                game.players = new List<PlayerData>();
                game.chats = new List<long>();


                collection.Add(game);
            }
            reader.Close();

            foreach (var game in collection) {
                game.owner = UserData.Get(game.ownerRaw);


                string sql2 = $"SELECT * FROM players WHERE gameid = {game.gameID}";
                DatabaseReader reader2 = Program.database.ExecuteReader(sql2);
                while (reader2.Read()) {
                    var player = new PlayerData();
                    player.gameID = reader2.GetInt64(0);
                    player.steamID = reader2.GetString(1);
                    player.turnOrder = reader2.GetInt32(2);

                    if (game.currentPlayerRaw == player.steamID) {
                        game.currentPlayer = player;
                    }
                    game.players.Add(player);
                }
                reader2.Close();

                foreach (var player in game.players) {
                    player.user = UserData.GetBySteamID(player.steamID);
                }


                string sql3 = $"SELECT chatid FROM gamechats WHERE gameid = {game.gameID}";
                DatabaseReader reader3 = Program.database.ExecuteReader(sql3);
                while (reader3.Read()) {

                    game.chats.Add(reader3.GetInt64(0));
                }
                reader3.Close();
            }

            return collection;
        }

        public void RemoveChat(long id) {
            string sql = $"DELETE FROM gamechats WHERE gameid = {gameID} AND chatid = {id}";
            Console.WriteLine(sql);
            int rows = Program.database.ExecuteNonQuery(sql);
        }
    }
}