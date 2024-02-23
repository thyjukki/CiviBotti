namespace CiviBotti.DataModels;

using System;
using System.Collections.Generic;
using Services;

public class SubData
{
    private readonly long _id;
    public readonly long SubId;
    public readonly long GameId;
    public readonly int Times;

    public SubData(long id, long subId, int times, long gameId) {
        _id = id;
        SubId = subId;
        Times = times;
        GameId = gameId;
    }

    public void InsertDatabase(Database db)
    {
        var sql = $"INSERT INTO subs (gameid, id, subid, times) values ({GameId}, '{_id}', '{SubId}', '{Times}')";
        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }

    public static List<SubData> Get(Database db, long id) {
        var sql = $"SELECT * FROM subs WHERE id = {id}";
        Console.WriteLine(sql);
        var reader = db.ExecuteReader(sql);


        var collection = new List<SubData>();
        while (reader.Read()) {

            var sub = new SubData(reader.GetInt64(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt64(0));
                
            collection.Add(sub);
        }
        reader.Close();
        return collection;
    }

    public void RemoveSub(Database db) {
        var sql = $"DELETE FROM subs WHERE gameid = {GameId} AND id = {_id} AND subid = {SubId}";
        Console.WriteLine(sql);
        db.ExecuteNonQuery(sql);
    }
}