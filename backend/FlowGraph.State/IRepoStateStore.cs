namespace FlowGraph.State;

public interface IRepoStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RepoState>> ListReposAsync(CancellationToken cancellationToken);
    Task<RepoState?> GetRepoAsync(string repoName, CancellationToken cancellationToken);
    Task UpsertRepoAsync(RepoState repo, CancellationToken cancellationToken);
    Task DeleteRepoAsync(string repoName, CancellationToken cancellationToken);
}

