using System.Data.SqlClient;
using System.Data.SQLite;

namespace CiviBotti {
    public class DatabaseReader {
        public SQLiteDataReader SqliteReader;
        public SqlDataReader SqlDataReader;
        private readonly Database.DatabaseType _type;

        /// <exception cref="DatabaseUnknownType" accessor="get">HasRows</exception>
        public bool HasRows {
            get {
                switch (_type) {
                    case Database.DatabaseType.SqLite:
                        return SqliteReader.HasRows;
                    case Database.DatabaseType.Sql:
                        return SqlDataReader.HasRows;
                    default:
                        throw new DatabaseUnknownType("HasRows");
                }
            }
        }

        public DatabaseReader(SQLiteDataReader reader) {
            SqliteReader = reader;
            _type = Database.DatabaseType.SqLite;
        }

        public DatabaseReader(SqlDataReader reader) {
            SqlDataReader = reader;
            _type = Database.DatabaseType.Sql;
        }

        /// <exception cref="DatabaseUnknownType">HasRows</exception>
        public bool Read() {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.Read();
                case Database.DatabaseType.Sql:
                    return SqlDataReader.Read();
                default:
                    throw new DatabaseUnknownType("HasRows");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt64</exception>
        public long GetInt64(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetInt64(v);
                case Database.DatabaseType.Sql:
                    return SqlDataReader.GetInt64(v);
                default:
                    throw new DatabaseUnknownType("GetInt64");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetString</exception>
        public string GetString(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetString(v);
                case Database.DatabaseType.Sql:
                    return SqlDataReader.GetString(v);
                default:
                    throw new DatabaseUnknownType("GetString");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt32</exception>
        public int GetInt32(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetInt32(v);
                case Database.DatabaseType.Sql:
                    return SqlDataReader.GetInt32(v);
                default:
                    throw new DatabaseUnknownType("GetInt32");
            }
        }

        /// <exception cref="DatabaseUnknownType">GetInt32</exception>
        public bool GetBit(int v) {
            switch (_type) {
                case Database.DatabaseType.SqLite:
                    return SqliteReader.GetBoolean(v);
                case Database.DatabaseType.Sql:
                    return SqlDataReader.GetBoolean(v);
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
                case Database.DatabaseType.Sql:
                    SqlDataReader.Close();
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
                case Database.DatabaseType.Sql:
                    return SqlDataReader.GetDataTypeName(0);
                default:
                    throw new DatabaseUnknownType("ToString");
            }
        }
    }
}