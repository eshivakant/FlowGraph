using Microsoft.Data.Sqlite;

namespace FlowGraph.State.Sqlite;

public sealed class SqliteIndexJobStore(SqliteConnectionFactory connectionFactory) : IIndexJobStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);
        await SqliteMigrations.EnsureCreatedAsync(conn, cancellationToken);
    }

    public async Task<IndexingJob> CreateJobAsync(string repoName, DateTimeOffset startedAt, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO indexing_jobs (repo_name, status, started_at, changed_files_count)
        VALUES ($repo, $status, $started_at, 0);
        SELECT last_insert_rowid();
        """;
        cmd.Parameters.AddWithValue("$repo", repoName);
        cmd.Parameters.AddWithValue("$status", "RUNNING");
        cmd.Parameters.AddWithValue("$started_at", startedAt.ToString("O"));

        var idObj = await cmd.ExecuteScalarAsync(cancellationToken);
        var id = Convert.ToInt64(idObj);

        return new IndexingJob(
            id,
            repoName,
            "RUNNING",
            StatusMessage: null,
            startedAt,
            CompletedAt: null,
            ChangedFilesCount: 0
        );
    }

    public async Task UpdateJobProgressAsync(long jobId, string statusMessage, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        UPDATE indexing_jobs
        SET status_message = $status_message
        WHERE id = $id;
        """;
        cmd.Parameters.AddWithValue("$status_message", statusMessage);
        cmd.Parameters.AddWithValue("$id", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CompleteJobAsync(long jobId, DateTimeOffset completedAt, int changedFilesCount, string status, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        UPDATE indexing_jobs
        SET status = $status,
            status_message = NULL,
            completed_at = $completed_at,
            changed_files_count = $changed_files_count,
            error = NULL
        WHERE id = $id;
        """;
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$completed_at", completedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$changed_files_count", changedFilesCount);
        cmd.Parameters.AddWithValue("$id", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FailJobAsync(long jobId, DateTimeOffset completedAt, string error, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        UPDATE indexing_jobs
        SET status = 'FAILED',
            status_message = NULL,
            completed_at = $completed_at,
            error = $error
        WHERE id = $id;
        """;
        cmd.Parameters.AddWithValue("$completed_at", completedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$error", error);
        cmd.Parameters.AddWithValue("$id", jobId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<IndexingJob>> ListJobsAsync(int take, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT id, repo_name, status, status_message, started_at, completed_at, changed_files_count
        FROM indexing_jobs
        ORDER BY started_at DESC
        LIMIT $take;
        """;
        cmd.Parameters.AddWithValue("$take", take);

        var results = new List<IndexingJob>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetInt64(0);
            var repo = reader.GetString(1);
            var status = reader.GetString(2);
            var statusMessage = reader.IsDBNull(3) ? null : reader.GetString(3);
            var startedAt = DateTimeOffset.Parse(reader.GetString(4));
            DateTimeOffset? completedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
            var changedFiles = reader.GetInt32(6);
            results.Add(new IndexingJob(id, repo, status, statusMessage, startedAt, completedAt, changedFiles));
        }

        return results;
    }

    public async Task<IndexingJob?> GetJobAsync(long jobId, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT id, repo_name, status, status_message, started_at, completed_at, changed_files_count
        FROM indexing_jobs
        WHERE id = $id
        LIMIT 1;
        """;
        cmd.Parameters.AddWithValue("$id", jobId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var id = reader.GetInt64(0);
        var repo = reader.GetString(1);
        var status = reader.GetString(2);
        var statusMessage = reader.IsDBNull(3) ? null : reader.GetString(3);
        var startedAt = DateTimeOffset.Parse(reader.GetString(4));
        DateTimeOffset? completedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
        var changedFiles = reader.GetInt32(6);
        return new IndexingJob(id, repo, status, statusMessage, startedAt, completedAt, changedFiles);
    }

    public async Task DeleteJobsForRepoAsync(string repoName, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM indexing_jobs WHERE repo_name = $repo";
        cmd.Parameters.AddWithValue("$repo", repoName);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

