﻿namespace CiviBotti.DataModels;

using System;
using System.Collections.Generic;
using Services;

public class UserData
{
    private static readonly List<UserData> Users = [];

    public long Id { get; }
    public string SteamId { get; }
    public string AuthKey { get; }

    public List<SubData> Subs { get; private set; } = [];


    public UserData(long id, string steamId, string authKey) {
        Id = id;
        SteamId = steamId;
        AuthKey = authKey;
        Users.Add(this);
    }

    public bool InsertDatabase(IDatabase db) {
        var result = false;

        var sql = $"INSERT INTO users (id, steamid, authkey) values ({Id}, '{SteamId}', '{AuthKey}')";
        Console.WriteLine(sql);
        var rows = db.ExecuteNonQuery(sql);

        if (rows == 1) {
            result = true;
        }
                
        return result;
    }

    public static bool CheckDatabase(IDatabase db, long id) {
        var sql = $"SELECT * FROM users WHERE id = {id}";
        var reader = db.ExecuteReader(sql);
        var result = reader.HasRows;
        reader.Close();
        return result;
    }

    public static UserData? Get(IDatabase db, long id) {
        var check = Users.Find(cachedUser => cachedUser.Id == id);
        if (check != null) return check;

        var sql = $"SELECT * FROM users WHERE id = {id}";
        Console.WriteLine(sql);
        var reader = db.ExecuteReader(sql);


        UserData user;
        if (reader.Read()) {
            user = new UserData(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
        }
        else {
            return null;
        }

        reader.Close();
        user.Subs = SubData.Get(db, user.Id);
        return user;
    }
        
    public static UserData? GetBySteamId(IDatabase db, string steamId) {
        var sql = $"SELECT * FROM users WHERE steamid = '{steamId}'";
        var reader = db.ExecuteReader(sql);


        UserData? user = null;
        if (reader.Read()) {
            var check = Users.Find(userData => userData.Id == reader.GetInt64(0));
            user = check ?? new UserData(reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
        }
            
        reader.Close();
        if (user != null)
        {
            user.Subs = SubData.Get(db, user.Id);
        }
        return user;
    }
}