namespace FlowGraph.Indexer;

public enum IndexMode
{
    Incremental,
    Full,
}

public sealed record ReindexRequest(
    string RepoName,
    string RemoteUrl,
    string Branch,
    IndexMode Mode,
    string? SolutionPath,
    string[]? IncludePatterns = null
);
