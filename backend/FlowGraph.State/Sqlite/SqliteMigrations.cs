using Microsoft.Data.Sqlite;

namespace FlowGraph.State.Sqlite;

public static class SqliteMigrations
{
    public static async Task EnsureCreatedAsync(SqliteConnection conn, CancellationToken cancellationToken)
    {
        // SQLite uses a coarse locking model; keep schema creation simple and idempotent.
        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;

        CREATE TABLE IF NOT EXISTS repos (
            repo_name TEXT PRIMARY KEY,
            remote_url TEXT NULL,
            branch TEXT NULL,
            solution_path TEXT NULL,
            last_indexed_commit TEXT NULL,
            last_indexed_at TEXT NULL,
            status TEXT NOT NULL,
            include_patterns TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS indexing_jobs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            repo_name TEXT NOT NULL,
            status TEXT NOT NULL,
            status_message TEXT NULL,
            started_at TEXT NOT NULL,
            completed_at TEXT NULL,
            changed_files_count INTEGER NOT NULL DEFAULT 0,
            error TEXT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_indexing_jobs_repo ON indexing_jobs(repo_name);
        CREATE INDEX IF NOT EXISTS idx_indexing_jobs_started ON indexing_jobs(started_at);

        CREATE TABLE IF NOT EXISTS config (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );
        """;

        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Ensure status_message column exists (migration for existing DBs)
        await EnsureColumnExistsAsync(conn, "indexing_jobs", "status_message", "TEXT NULL", cancellationToken);
        
        // Ensure configuration columns exist for repos table
        await EnsureColumnExistsAsync(conn, "repos", "remote_url", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(conn, "repos", "branch", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(conn, "repos", "solution_path", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(conn, "repos", "include_patterns", "TEXT NULL", cancellationToken);
    }

    private static async Task EnsureColumnExistsAsync(SqliteConnection conn, string table, string column, string type, CancellationToken cancellationToken)
    {
        try
        {
            var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type};";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1) // 1 = SQLITE_ERROR, usually "duplicate column name"
        {
            // Column already exists, ignore.
        }
    }
}

