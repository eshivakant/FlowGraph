using Microsoft.Extensions.Logging;

namespace FlowGraph.Graph;

public sealed class NoopGraphClient(ILogger<NoopGraphClient> logger) : IGraphWriter, IGraphQueryService
{
    public Task UpsertAsync(string repoName, string commitSha, IReadOnlyList<GraphTriple> triples, Func<string, Task>? progress, CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph upsert invoked with {TripleCount} triples.", triples.Count);
        return Task.CompletedTask;
    }

    public Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph delete invoked.");
        return Task.CompletedTask;
    }

    public Task WipeAsync(CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph wipe invoked.");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GraphEntity>> SearchAsync(string query, string[]? repos, int take, CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph search invoked.");
        return Task.FromResult<IReadOnlyList<GraphEntity>>(Array.Empty<GraphEntity>());
    }

    public Task<TraceResult> TraceAsync(string startId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph trace invoked.");
        return Task.FromResult(new TraceResult(null, Array.Empty<GraphTriple>()));
    }

    public Task<IReadOnlyList<GraphEntity>> ImpactAsync(string changeId, string[]? repos, int maxDepth, CancellationToken cancellationToken)
    {
        logger.LogWarning("Noop graph impact invoked.");
        return Task.FromResult<IReadOnlyList<GraphEntity>>(Array.Empty<GraphEntity>());
    }
}
