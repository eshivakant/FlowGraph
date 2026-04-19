using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FlowGraph.State.Sqlite;

public sealed class SqliteRepoStateStore(SqliteConnectionFactory connectionFactory) : IRepoStateStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);
        await SqliteMigrations.EnsureCreatedAsync(conn, cancellationToken);
    }

    public async Task<IReadOnlyList<RepoState>> ListReposAsync(CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT repo_name, remote_url, branch, solution_path, last_indexed_commit, last_indexed_at, status, include_patterns
        FROM repos
        ORDER BY repo_name;
        """;

        var results = new List<RepoState>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var repo = reader.GetString(0);
            var remoteUrl = reader.IsDBNull(1) ? null : reader.GetString(1);
            var branch = reader.IsDBNull(2) ? null : reader.GetString(2);
            var solPath = reader.IsDBNull(3) ? null : reader.GetString(3);
            var lastCommit = reader.IsDBNull(4) ? null : reader.GetString(4);
            DateTimeOffset? lastAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
            var status = reader.GetString(6);
            var includesJson = reader.IsDBNull(7) ? null : reader.GetString(7);
            var includes = includesJson is null ? null : JsonSerializer.Deserialize<string[]>(includesJson);

            results.Add(new RepoState(repo, remoteUrl, branch, solPath, lastCommit, lastAt, status, includes));
        }

        return results;
    }

    public async Task<RepoState?> GetRepoAsync(string repoName, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        SELECT repo_name, remote_url, branch, solution_path, last_indexed_commit, last_indexed_at, status, include_patterns
        FROM repos
        WHERE repo_name = $repo
        LIMIT 1;
        """;
        cmd.Parameters.AddWithValue("$repo", repoName);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var repo = reader.GetString(0);
        var remoteUrl = reader.IsDBNull(1) ? null : reader.GetString(1);
        var branch = reader.IsDBNull(2) ? null : reader.GetString(2);
        var solPath = reader.IsDBNull(3) ? null : reader.GetString(3);
        var lastCommit = reader.IsDBNull(4) ? null : reader.GetString(4);
        DateTimeOffset? lastAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
        var status = reader.GetString(6);
        var includesJson = reader.IsDBNull(7) ? null : reader.GetString(7);
        var includes = includesJson is null ? null : JsonSerializer.Deserialize<string[]>(includesJson);

        return new RepoState(repo, remoteUrl, branch, solPath, lastCommit, lastAt, status, includes);
    }

    public async Task UpsertRepoAsync(RepoState repo, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = """
        INSERT INTO repos (repo_name, remote_url, branch, solution_path, last_indexed_commit, last_indexed_at, status, include_patterns)
        VALUES ($repo_name, $remote_url, $branch, $solution_path, $last_commit, $last_at, $status, $include_patterns)
        ON CONFLICT(repo_name) DO UPDATE SET
            remote_url = excluded.remote_url,
            branch = excluded.branch,
            solution_path = excluded.solution_path,
            last_indexed_commit = excluded.last_indexed_commit,
            last_indexed_at = excluded.last_indexed_at,
            status = excluded.status,
            include_patterns = excluded.include_patterns;
        """;
        cmd.Parameters.AddWithValue("$repo_name", repo.RepoName);
        cmd.Parameters.AddWithValue("$remote_url", (object?)repo.RemoteUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$branch", (object?)repo.Branch ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$solution_path", (object?)repo.SolutionPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_commit", (object?)repo.LastIndexedCommit ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$last_at", repo.LastIndexedAt is null ? DBNull.Value : repo.LastIndexedAt.Value.ToString("O"));
        cmd.Parameters.AddWithValue("$status", repo.Status);
        cmd.Parameters.AddWithValue("$include_patterns", repo.IncludePatterns is null ? DBNull.Value : JsonSerializer.Serialize(repo.IncludePatterns));

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
    
    public async Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken)
    {
        await using var conn = connectionFactory.Create();
        await conn.OpenAsync(cancellationToken);

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM repos WHERE repo_name = $repo";
        cmd.Parameters.AddWithValue("$repo", repoName);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}

