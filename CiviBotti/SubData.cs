using System;
using System.Collections.Generic;

namespace CiviBotti
{
    public class SubData
    {
        public readonly long Id;
        public readonly long SubId;
        public readonly GameData Game;
        public readonly int Times;

        public SubData(long id, long subId, int times, GameData game) {
            Id = id;
            SubId = subId;
            Times = times;
            Game = game;
        }

        public void InsertDatabase(Database db)
        {
            var sql = $"INSERT INTO subs (gameid, id, subid, times) values ({Game.GameId}, '{Id}', '{SubId}', '{Times}')";
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }

        public static List<SubData> Get(Database db, long id) {
            var sql = $"SELECT * FROM subs WHERE id = {id}";
            Console.WriteLine(sql);
            var reader = db.ExecuteReader(sql);


            var collection = new List<SubData>();
            while (reader.Read()) {
                var gameId = reader.GetInt64(0);

                var game = SubProgram.Games.Find(_ => _.GameId == gameId);

                if (game == null) {
                    Console.WriteLine($"Game id: {gameId} missing");
                    continue;
                }

                var sub = new SubData(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3), game);
                
                collection.Add(sub);
            }
            reader.Close();
            return collection;
        }

        public void RemoveSub(Database db) {
            var sql = $"DELETE FROM subs WHERE gameid = {Game.GameId} AND id = {Id} AND subid = {SubId}";
            Console.WriteLine(sql);
            db.ExecuteNonQuery(sql);
        }
    }
}