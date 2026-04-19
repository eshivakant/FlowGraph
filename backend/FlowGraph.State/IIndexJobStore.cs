namespace FlowGraph.State;

public interface IIndexJobStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IndexingJob> CreateJobAsync(string repoName, DateTimeOffset startedAt, CancellationToken cancellationToken);
    Task UpdateJobProgressAsync(long jobId, string statusMessage, CancellationToken cancellationToken);
    Task CompleteJobAsync(long jobId, DateTimeOffset completedAt, int changedFilesCount, string status, CancellationToken cancellationToken);
    Task FailJobAsync(long jobId, DateTimeOffset completedAt, string error, CancellationToken cancellationToken);

    Task<IReadOnlyList<IndexingJob>> ListJobsAsync(int take, CancellationToken cancellationToken);
    Task<IndexingJob?> GetJobAsync(long jobId, CancellationToken cancellationToken);
    Task DeleteJobsForRepoAsync(string repoName, CancellationToken cancellationToken);
}

