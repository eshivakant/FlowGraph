namespace FlowGraph.State;

public sealed record RepoState(
    string RepoName,
    string? RemoteUrl,
    string? Branch,
    string? SolutionPath,
    string? LastIndexedCommit,
    DateTimeOffset? LastIndexedAt,
    string Status,
    string[]? IncludePatterns = null
);

public sealed record IndexingJob(
    long Id,
    string RepoName,
    string Status,
    string? StatusMessage,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int ChangedFilesCount
);
