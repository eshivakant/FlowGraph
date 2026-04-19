namespace FlowGraph.Graph;

public enum GraphRelation
{
    Calls,
    Publishes,
    Consumes,
    Handles,
    DependsOn,
    UsesTopic,
    Triggers,
    Implements,
    Contains,
    Inherits,
}

public sealed record GraphEntity(string Kind, string Id, IReadOnlyDictionary<string, object?> Properties);

public sealed record GraphTriple(GraphEntity Source, GraphRelation Relation, GraphEntity Target, IReadOnlyDictionary<string, object?> Properties);

public interface IGraphWriter
{
    Task UpsertAsync(string repoName, string commitSha, IReadOnlyList<GraphTriple> triples, Func<string, Task>? progress, CancellationToken cancellationToken);
    Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken);
    Task WipeAsync(CancellationToken cancellationToken);
}

public sealed record TraceResult(GraphEntity? StartNode, IReadOnlyList<GraphTriple> Triples);

public interface IGraphQueryService
{
    Task<IReadOnlyList<GraphEntity>> SearchAsync(string query, string[]? repos, int take, CancellationToken cancellationToken);
    Task<TraceResult> TraceAsync(string startId, string[]? repos, int maxDepth, CancellationToken cancellationToken);
    Task<IReadOnlyList<GraphEntity>> ImpactAsync(string changeId, string[]? repos, int maxDepth, CancellationToken cancellationToken);
}
