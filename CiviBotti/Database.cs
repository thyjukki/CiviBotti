using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CiviBotti {
    public class Database {
        DatabaseType type;

        private SqlConnection sqlConnection;
        private SQLiteConnection sqliteConnection;

        public ConnectionState State {
            get {
                switch (type) {
                    case DatabaseType.SQLite:
                        return sqliteConnection.State;
                    case DatabaseType.SQL:
                        return sqlConnection.State;
                    default:
                        throw new DatabaseUnknownType("ConnectionState");
                }
            }
        }

        public enum DatabaseType {
            SQLite,
            SQL
        }

        public Database(DatabaseType t) {
            type = t;

            switch (type) {
                case DatabaseType.SQLite:
                    if (!System.IO.File.Exists("database.sqlite")) {
                        SQLiteConnection.CreateFile("database.sqlite");
                        sqliteConnection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                        sqliteConnection.Open();
                        SQLiteCommand command = new SQLiteCommand("CREATE TABLE users (id bigint NOT NULL, steamid VARCHAR(20), authkey VARCHAR(20), PRIMARY KEY(id))", sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE games (gameid bigint NOT NULL, ownerid NOT NULL, name VARCHAR(40), currentp VARCHAR(20), PRIMARY KEY(gameid), FOREIGN KEY(ownerid) REFERENCES users(id))", sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE players (gameid bigint NOT NULL, steamid VARCHAR(20), turnorder INT, PRIMARY KEY(gameid, steamid))", sqliteConnection);
                        command.ExecuteNonQuery();
                        command = new SQLiteCommand("CREATE TABLE gamechats (gameid bigint NOT NULL, chatid bigint NOT NULL, PRIMARY KEY(gameid, chatid))", sqliteConnection);
                        command.ExecuteNonQuery();
                        sqliteConnection.Close();
                    } else {
                        sqliteConnection = new SQLiteConnection("Data Source=database.sqlite;Version=3;");
                    }
                    break;
                case DatabaseType.SQL:
                    SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

                    ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
                    configMap.ExeConfigFilename = "bot.config";
                    Configuration config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

                    builder.DataSource = config.AppSettings.Settings["SqlHost"].Value;
                    builder.UserID = config.AppSettings.Settings["SqlUSer"].Value;
                    builder.Password = config.AppSettings.Settings["SqlPassword"].Value;
                    builder.InitialCatalog = config.AppSettings.Settings["SqlDatabase"].Value;
                    sqlConnection = new SqlConnection(builder.ConnectionString);
                    break;
                default:
                    throw new DatabaseUnknownType("Database");
            }
        }

        public int ExecuteNonQuery(string sql) {
            Open();

            try {
                switch (type) {
                    case DatabaseType.SQLite:
                        SQLiteCommand sqliteCommand = new SQLiteCommand(sql, sqliteConnection);
                        return sqliteCommand.ExecuteNonQuery();
                    case DatabaseType.SQL:
                        SqlCommand sqlCommand = new SqlCommand(sql, sqlConnection);
                        return sqlCommand.ExecuteNonQuery();
                    default:
                        throw new DatabaseUnknownType("ExecuteNonQuery " + sql);
                }
            } catch (SQLiteException ex) {
                Console.WriteLine("Database SQLite ExecuteNonQuery exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database SQLite ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}");
            } catch (SqlException ex) {
                Console.WriteLine($"Database Sql ExecuteNonQuery exception with {sql}\n{ex.ErrorCode}");
                throw new DatabaseQueryFail();
            }
        }

        public DatabaseReader ExecuteReader(string sql) {
            Open();

            try {
                switch (type) {
                    case DatabaseType.SQLite:
                        SQLiteCommand sqliteCommand = new SQLiteCommand(sql, sqliteConnection);
                        return new DatabaseReader(sqliteCommand.ExecuteReader());
                    case DatabaseType.SQL:
                        SqlCommand sqlCommand = new SqlCommand(sql, sqlConnection);
                        return new DatabaseReader(sqlCommand.ExecuteReader());
                    default:
                        throw new DatabaseUnknownType("ExecuteReader " + sql);
                }
            } catch (SQLiteException ex) {
                Console.WriteLine("Database SQLite ExecuteReader exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database SQLite ExecuteReader exception with {sql}\n{ex.ErrorCode}");
            } catch (SqlException ex) {
                Console.WriteLine("Database SQL ExecuteReader exception " + ex.ErrorCode);
                throw new DatabaseQueryFail($"Database Sql ExecuteReader exception with {sql}\n{ex.ErrorCode}");
            }
        }

        public void Open() {
            if (State != ConnectionState.Open) {
                try {
                    switch (type) {
                        case DatabaseType.SQLite:
                            sqliteConnection.Open();
                            break;
                        case DatabaseType.SQL:
                            sqlConnection.Open();
                            break;
                        default:
                            throw new DatabaseUnknownType("Open");
                    }
                } catch (SQLiteException ex) {
                    Console.WriteLine("Database SQLite Open exception " + ex.ErrorCode);
                    throw new DatabaseOpenFail();
                } catch (SqlException ex) {
                    Console.WriteLine("Database SQL Open exception " + ex.ErrorCode);
                    throw new DatabaseOpenFail();
                }
            }
        }
    }
}
