using System.Data.Common;

namespace CiviBotti.Services;

public interface IDatabase
{
    int ExecuteNonQuery(string sql);
    DbDataReader ExecuteReader(string sql);
}