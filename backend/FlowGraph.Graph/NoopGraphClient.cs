namespace FlowGraph.Graph;

public sealed class NoopGraphClient : IGraphWriter, IGraphQueryService
{
    public Task UpsertAsync(string repoName, string commitSha, IReadOnlyList<GraphTriple> triples, Func<string, Task>? progress, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task WipeAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task<IReadOnlyList<GraphEntity>> SearchAsync(string query, string[]? repos, int take, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GraphEntity>>(Array.Empty<GraphEntity>());

    public Task<TraceResult> TraceAsync(string startId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
        => Task.FromResult(new TraceResult(null, Array.Empty<GraphTriple>()));

    public Task<IReadOnlyList<GraphEntity>> ImpactAsync(string changeId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<GraphEntity>>(Array.Empty<GraphEntity>());
}

