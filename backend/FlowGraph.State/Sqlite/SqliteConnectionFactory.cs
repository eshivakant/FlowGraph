using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FlowGraph.State.Sqlite;

public sealed class SqliteConnectionFactory(string connectionString, ILogger<SqliteConnectionFactory> logger)
{
    public SqliteConnection Create()
    {
        logger.LogDebug("Creating SQLite connection.");
        var conn = new SqliteConnection(connectionString);
        conn.DefaultTimeout = 30;
        return conn;
    }
}
