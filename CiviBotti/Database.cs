using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;

namespace CiviBotti {
    public class Database {
        readonly DatabaseType _type;

        private readonly SqlConnection _sqlConnection;
        private readonly SQLiteConnection _sqliteConnection;

        /// <exception cref="DatabaseUnknownType" accessor="get">Unknown database connection</exception>
        public ConnectionState State {
            get {
                switch (_type) {
                    case DatabaseType.SqLite:
                        return _sqliteConnection.State;
                    case DatabaseType.Sql:
                        return _sqlConnection.State;
                    default:
                        throw new DatabaseUnknownType("ConnectionState");
                }
            }
        }

        public enum DatabaseType {
            SqLite,
            Sql
        }

        /// <exception cref="DatabaseUnknownType">Unknown database connection</exception>
        public Database(DatabaseType t) {
            _type = t;

            switch (_type) {
                case DatabaseType.SqLite:
                    if (!System.IO.File.Exists("database.sqlite")) {
                        SQLiteConnection.CreateFile("database.sqlite");
                        _sqliteConnection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                        _sqliteConnection.Open();
                        var command = new SQLiteCommand("CREATE TABLE users (id bigint NOT NULL, steamid VARCHAR(20), authkey VARCHAR(20), PRIMARY KEY(id))", _sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE games (gameid bigint NOT NULL, ownerid NOT NULL, name VARCHAR(40), currentp VARCHAR(20), PRIMARY KEY(gameid), FOREIGN KEY(ownerid) REFERENCES users(id))", _sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE players (gameid bigint NOT NULL, steamid VARCHAR(20), turnorder INT, PRIMARY KEY(gameid, steamid))", _sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE gamechats (gameid bigint NOT NULL, chatid bigint NOT NULL, PRIMARY KEY(gameid, chatid))", _sqliteConnection);
                        command.ExecuteNonQuery();
                        _sqliteConnection.Close();
                    } else {
                        _sqliteConnection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                    }
                    break;
                case DatabaseType.Sql:
                    var builder = new SqlConnectionStringBuilder();

                    var configMap = new ExeConfigurationFileMap();
                    configMap.ExeConfigFilename = "bot.config";
                    try
                    {
                        var config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);
                        builder.DataSource = config.AppSettings.Settings["SqlHost"].Value;
                        builder.UserID = config.AppSettings.Settings["SqlUSer"].Value;
                        builder.Password = config.AppSettings.Settings["SqlPassword"].Value;
                        builder.InitialCatalog = config.AppSettings.Settings["SqlDatabase"].Value;
                        _sqlConnection = new SqlConnection(builder.ConnectionString);
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
                    throw new DatabaseUnknownType("Database");
            }
        }

        /// <exception cref="DatabaseUnknownType">Condition.</exception>
        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public int ExecuteNonQuery(string sql) {
            Open();

            try {
                switch (_type) {
                    case DatabaseType.SqLite:
                        var sqliteCommand = new SQLiteCommand(sql, _sqliteConnection);
                        return sqliteCommand.ExecuteNonQuery();
                    case DatabaseType.Sql:
                        var sqlCommand = new SqlCommand(sql, _sqlConnection);
                        return sqlCommand.ExecuteNonQuery();
                    default:
                        throw new DatabaseUnknownType("ExecuteNonQuery " + sql);
                }
            } catch (SQLiteException ex) {
                Console.WriteLine("Database SQLite ExecuteNonQuery exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database SQLite ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}", ex);
            } catch (SqlException ex) {
                Console.WriteLine($"Database Sql ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}");
                throw new DatabaseQueryFail("See the inner exception for details.", ex);
            }
        }

        /// <exception cref="DatabaseQueryFail">Condition.</exception>
        public DatabaseReader ExecuteReader(string sql) {
            Open();

            try {
                switch (_type) {
                    case DatabaseType.SqLite:
                        var sqliteCommand = new SQLiteCommand(sql, _sqliteConnection);
                        return new DatabaseReader(sqliteCommand.ExecuteReader());
                    case DatabaseType.Sql:
                        var sqlCommand = new SqlCommand(sql, _sqlConnection);
                        return new DatabaseReader(sqlCommand.ExecuteReader());
                    default:
                        throw new DatabaseUnknownType("ExecuteReader " + sql);
                }
            } catch (SQLiteException ex) {
                Console.WriteLine("Database SQLite ExecuteReader exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database SQLite ExecuteReader exception with {sql}\n{ex.ErrorCode}", ex);
            } catch (SqlException ex) {
                Console.WriteLine("Database SQL ExecuteReader exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database Sql ExecuteReader exception with {sql}\n{ex.ErrorCode}", ex);
            }
        }

        /// <exception cref="DatabaseOpenFail">Condition.</exception>
        /// <exception cref="DatabaseUnknownType">Open</exception>
        public void Open()
        {
            if (State == ConnectionState.Open) return;
            try {
                switch (_type) {
                    case DatabaseType.SqLite:
                        _sqliteConnection.Open();
                        break;
                    case DatabaseType.Sql:
                        _sqlConnection.Open();
                        break;
                    default:
                        throw new DatabaseUnknownType("Open");
                }
            } catch (SQLiteException ex) {
                Console.WriteLine("Database SQLite Open exception " + ex.ErrorCode);
                throw new DatabaseOpenFail("See the inner exception for details.", ex);
            } catch (SqlException ex) {
                Console.WriteLine("Database SQL Open exception " + ex.ErrorCode);
                throw new DatabaseOpenFail("See the inner exception for details.", ex);
            }
        }
    }
}
