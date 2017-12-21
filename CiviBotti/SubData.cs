using System;
using System.Collections.Generic;

namespace CiviBotti
{
    public class SubData
    {
        public long Id;
        public long SubId;
        public GameData Game;
        public int Times;

        public bool InsertDatabase(bool open)
        {
            var result = false;

            var sql = $"INSERT INTO subs (gameid, id, subid, times) values ({Game.GameId}, '{Id}', '{SubId}', '{Times}')";
            Console.WriteLine(sql);
            var rows = Program.Database.ExecuteNonQuery(sql);

            if (rows == 1)
            {
                result = true;
            }

            return result;
        }

        public static List<SubData> Get(long id)
        {
            var sql = $"SELECT * FROM subs WHERE id = {id}";
            Console.WriteLine(sql);
            var reader = Program.Database.ExecuteReader(sql);


            var collection = new List<SubData>();
            while (reader.Read()) {
                var gameId = reader.GetInt64(0);

                var game = Program.Games.Find(_ => _.GameId == gameId);

                if (game == null) {
                    Console.WriteLine($"Game id: {gameId} missing");
                    continue;
                }

                var sub = new SubData
                {
                    Id = reader.GetInt64(1),
                    SubId = reader.GetInt64(2),
                    Times = reader.GetInt32(3),
                    Game = game
                };



                collection.Add(sub);
            }
            reader.Close();
            return collection;
        }

        public void RemoveSub() {
            var sql = $"DELETE FROM subs WHERE gameid = {Game.GameId} AND id = {Id} AND subid = {SubId}";
            Console.WriteLine(sql);
            Program.Database.ExecuteNonQuery(sql);
        }
    }
}