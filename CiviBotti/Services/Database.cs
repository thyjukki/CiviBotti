namespace CiviBotti.Services;

using System;
using System.Configuration;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Data.SQLite;
using Configurations;
using Microsoft.Extensions.Options;
using MySql.Data.MySqlClient;

public class Database : IDatabase
{
    private readonly DbConnection _connection;

    public enum DatabaseType {
        SqLite,
        MySql
    }

    public Database(IOptions<BotConfiguration> configuration) {
        _connection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
        var configValue = configuration.Value;
        switch (configValue.DbType) {
            case DatabaseType.SqLite:
                if (!System.IO.File.Exists("database.sqlite")) {
                    SQLiteConnection.CreateFile("database.sqlite");
                    var sqLiteConnection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                    _connection = sqLiteConnection;
                    sqLiteConnection.Open();
                    var command = new SQLiteCommand("CREATE TABLE users (id bigint NOT NULL, steamid VARCHAR(20), authkey VARCHAR(20), PRIMARY KEY(id))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    command = new SQLiteCommand("CREATE TABLE games (gameid bigint NOT NULL, ownerid bigint NOT NULL, name VARCHAR(40), currentp VARCHAR(20), notified BIT NOT NULL DEFAULT '1', turnid VARCHAR(20), PRIMARY KEY(gameid), FOREIGN KEY(ownerid) REFERENCES users(id))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    command = new SQLiteCommand("CREATE TABLE players (gameid bigint NOT NULL, steamid VARCHAR(20), turnorder INT, nexteta VARCHAR(20), PRIMARY KEY(gameid, steamid))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    command = new SQLiteCommand("CREATE TABLE gamechats (gameid bigint NOT NULL, chatid bigint NOT NULL, PRIMARY KEY(gameid, chatid))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    command = new SQLiteCommand("CREATE TABLE quotes (gameid bigint NOT NULL, chatid bigint NOT NULL, data TEXT, PRIMARY KEY(gameid, chatid))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    command = new SQLiteCommand("CREATE TABLE subs (gameid bigint NOT NULL, id bigint NOT NULL, subid bigint NOT NULL, times int NOT NULL, PRIMARY KEY(gameid, id, subid))", sqLiteConnection);
                    command.ExecuteNonQuery();
                    sqLiteConnection.Close();
                } else {
                    _connection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                }
                break;
            case DatabaseType.MySql:
                var builder = new SqlConnectionStringBuilder();

                try
                {
                    builder.DataSource = configValue.Host;
                    builder.UserID = configValue.User;
                    builder.Password = configValue.Password;
                    builder.InitialCatalog = configValue.Database;
                    _connection = new MySqlConnection(builder.ConnectionString);
                }
                catch (ConfigurationErrorsException ex)
                {
                    Console.WriteLine("Database configuration file failed: " + ex.Message);
                }
                catch (ArgumentNullException ex) {
                    Console.WriteLine("Database configuration file null exception: " + ex.Message);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(configuration), configuration, "Wrong database type defined");
        }
    }

    public int ExecuteNonQuery(string sql) {
        try {
            if (_connection.State != ConnectionState.Open) {
                _connection.Open();
            }
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteNonQuery();
        } catch (DbException ex) {
            Console.WriteLine($"Database ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}");
            throw;
        }
    }

    public DbDataReader ExecuteReader(string sql) {

        try {
            if (_connection.State != ConnectionState.Open) {
                _connection.Open();
            }
            var command = _connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteReader();
        } catch (DbException ex) {
            Console.WriteLine($"Database ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}");
            throw;
        }
    }
}