using Microsoft.Data.Sqlite;

namespace FlowGraph.State.Sqlite;

public sealed class SqliteConnectionFactory(string connectionString)
{
    public SqliteConnection Create()
    {
        var conn = new SqliteConnection(connectionString);
        conn.DefaultTimeout = 30;
        return conn;
    }
}

