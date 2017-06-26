using System;
using System.Data.SqlClient;
using System.Data.SQLite;

namespace CiviBotti {
    public class DatabaseReader {
        public SQLiteDataReader sqliteReader;
        public SqlDataReader sqlDataReader;
        private Database.DatabaseType type;

        public bool HasRows {
            get {
                switch (type) {
                    case Database.DatabaseType.SQLite:
                        return sqliteReader.HasRows;
                    case Database.DatabaseType.SQL:
                        return sqlDataReader.HasRows;
                    default:
                        throw new DatabaseUnknownType("HasRows");
                }
            }
        }

        public DatabaseReader(SQLiteDataReader reader) {
            this.sqliteReader = reader;
            type = Database.DatabaseType.SQLite;
        }

        public DatabaseReader(SqlDataReader reader) {
            this.sqlDataReader = reader;
            type = Database.DatabaseType.SQL;
        }

        public bool Read() {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    return sqliteReader.Read();
                case Database.DatabaseType.SQL:
                    return sqlDataReader.Read();
                default:
                    throw new DatabaseUnknownType("HasRows");
            }
        }

        public long GetInt64(int v) {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    return sqliteReader.GetInt64(v);
                case Database.DatabaseType.SQL:
                    return sqlDataReader.GetInt64(v);
                default:
                    throw new DatabaseUnknownType("GetInt64");
            }
        }

        public string GetString(int v) {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    return sqliteReader.GetString(v);
                case Database.DatabaseType.SQL:
                    return sqlDataReader.GetString(v);
                default:
                    throw new DatabaseUnknownType("GetString");
            }
        }

        public int GetInt32(int v) {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    return sqliteReader.GetInt32(v);
                case Database.DatabaseType.SQL:
                    return sqlDataReader.GetInt32(v);
                default:
                    throw new DatabaseUnknownType("GetInt32");
            }
        }

        public void Close() {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    sqliteReader.Close();
                    break;
                case Database.DatabaseType.SQL:
                    sqlDataReader.Close();
                    break;
                default:
                    throw new DatabaseUnknownType("Close");
            }
        }



        public override string ToString() {
            switch (type) {
                case Database.DatabaseType.SQLite:
                    return sqliteReader.ToString();
                case Database.DatabaseType.SQL:
                    return sqlDataReader.GetDataTypeName(0);
                default:
                    throw new DatabaseUnknownType("ToString");
            }
        }
    }
}