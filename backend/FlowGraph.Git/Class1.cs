namespace FlowGraph.Git;

public sealed record RepoCheckout(string LocalPath, string HeadCommit);

public interface IGitService
{
    Task<RepoCheckout> EnsureRepoAsync(string repoName, string remoteUrl, string branch, string checkoutRoot, CancellationToken cancellationToken);
    Task<bool> CommitExistsAsync(string localRepoPath, string commitSha, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetChangedFilesAsync(string localRepoPath, string fromCommit, string toCommit, CancellationToken cancellationToken);
}

