using System.Data.SqlClient;
using System.Data.SQLite;
using MySql.Data.MySqlClient;

namespace CiviBotti {
    public class DatabaseReader {
        public SQLiteDataReader SqliteReader;
        public MySqlDataReader MySqlDataReader;
        private readonly Database.DatabaseType _type;

        /// <exception cref="DatabaseUnknownType" accessor="get">HasRows</exception>
        public bool HasRows {
            get {
                switch (_type) {
                    case Database.DatabaseType.SqLite:
                        return SqliteReader.HasRows;
                    case Database.DatabaseType.MySql:
                        return MySqlDataReader.HasRows;
                    default:
                        throw new DatabaseUnknownType("HasRows");
                }
            }
        }

        public DatabaseReader(SQLiteDataReader reader) {
            SqliteReader = reader;
            _type = Database.DatabaseType.SqLite;
        }

        public DatabaseReader(MySqlDataReader reader) {
            MySqlDataReader = reader;
            _type = Database.DatabaseType.MySql;
        }

        /// <exception cref="DatabaseUnknownType">HasRows</exception>
        public bool Read() {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.Read();
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.Read();
                default:
                    throw new DatabaseUnknownType("HasRows");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt64</exception>
        public long GetInt64(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetInt64(v);
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.GetInt64(v);
                default:
                    throw new DatabaseUnknownType("GetInt64");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetString</exception>
        public string GetString(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetString(v);
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.GetString(v);
                default:
                    throw new DatabaseUnknownType("GetString");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt32</exception>
        public int GetInt32(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetInt32(v);
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.GetInt32(v);
                default:
                    throw new DatabaseUnknownType("GetInt32");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt32</exception>
        public bool GetBit(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetBoolean(v);
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.GetBoolean(v);
                default:
                    throw new DatabaseUnknownType("GetInt32");
            }
        }

        /// <exception cref="DatabaseUnknownType">Close</exception>
        public void Close() {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    SqliteReader.Close();
                    break;
                case Database.DatabaseType.MySql:
                    MySqlDataReader.Close();
                    break;
                default:
                    throw new DatabaseUnknownType("Close");
            }
        }


        /// <exception cref="DatabaseUnknownType">ToString</exception>
        public override string ToString() {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.ToString();
                case Database.DatabaseType.MySql:
                    return MySqlDataReader.GetDataTypeName(0);
                default:
                    throw new DatabaseUnknownType("ToString");
            }
        }
    }
}