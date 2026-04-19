using FlowGraph.Blobs;
using FlowGraph.Git;
using FlowGraph.Graph;
using FlowGraph.Roslyn;
using FlowGraph.State;

namespace FlowGraph.Indexer;

public sealed class RepoIndexer(
    IGitService git,
    IRoslynIngestor roslyn,
    IGraphWriter graphWriter,
    IRepoStateStore repoState,
    IIndexJobStore jobs,
    IBlobStore blobs,
    IndexerOptions options) : IRepoIndexer
{
    public async Task<IndexingJob> ReindexAsync(ReindexRequest request, CancellationToken cancellationToken)
    {
        await repoState.InitializeAsync(cancellationToken);
        await jobs.InitializeAsync(cancellationToken);

        var startedAt = DateTimeOffset.UtcNow;
        var job = await jobs.CreateJobAsync(request.RepoName, startedAt, cancellationToken);
        await repoState.UpsertRepoAsync(new RepoState(request.RepoName, request.RemoteUrl, request.Branch, request.SolutionPath, null, null, "INDEXING", request.IncludePatterns), cancellationToken);

        // Fire and forget the background processing
        _ = Task.Run(async () =>
        {
            try
            {
                await RunIndexingPipelineAsync(job, request, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] Indexing failed for {request.RepoName}: {ex}");
            }
        });

        return job;
    }

    private async Task RunIndexingPipelineAsync(IndexingJob job, ReindexRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await TryUpdateProgress(job.Id, "Checking out repository...", cancellationToken);
            var checkout = await git.EnsureRepoAsync(
                request.RepoName,
                request.RemoteUrl,
                request.Branch,
                options.CheckoutRoot,
                cancellationToken);

            await TryUpdateProgress(job.Id, "Calculating changed files...", cancellationToken);

            var head = checkout.HeadCommit;
            var state = await repoState.GetRepoAsync(request.RepoName, cancellationToken);
            var lastCommit = state?.LastIndexedCommit;

            var fullScan =
                request.Mode == IndexMode.Full ||
                string.IsNullOrWhiteSpace(lastCommit) ||
                !(await git.CommitExistsAsync(checkout.LocalPath, lastCommit!, cancellationToken));

            IReadOnlyList<string> changedFiles;
            if (fullScan)
            {
                changedFiles = Directory.GetFiles(checkout.LocalPath, "*.cs", SearchOption.AllDirectories)
                    .Select(p => Path.GetRelativePath(checkout.LocalPath, p))
                    .ToArray();
            }
            else
            {
                changedFiles = await git.GetChangedFilesAsync(checkout.LocalPath, lastCommit!, head, cancellationToken);
            }

            if (changedFiles.Count == 0)
            {
                await TryUpdateProgress(job.Id, "No changes detected. Finishing early.", cancellationToken);
                await CompleteJobAsync(job, head, 0, request, cancellationToken);
                return;
            }

            await TryUpdateProgress(job.Id, $"Ingesting {changedFiles.Count} files with Roslyn...", cancellationToken);

            // Persist changed files artifact (useful for debugging).
            var artifactPath = $"diffs/{request.RepoName}/{job.Id}/{head}.files.txt";
            try
            {
                await using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, changedFiles)));
                await blobs.WriteAsync(artifactPath, ms, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Failed to write changed files artifact: {ex.Message}");
            }

            var triples = await roslyn.IngestAsync(
                new RoslynIngestionRequest(
                    RepoName: request.RepoName,
                    RepoRootPath: checkout.LocalPath,
                    CommitSha: head,
                    SolutionPath: request.SolutionPath,
                    ChangedFiles: changedFiles),
                msg => TryUpdateProgress(job.Id, msg, cancellationToken),
                cancellationToken);

            // Filter noise based on IncludePatterns
            if (request.IncludePatterns != null && request.IncludePatterns.Length > 0)
            {
                var originalCount = triples.Count;
                triples = triples.Where(t => IsMatch(t.Source.Id, request.IncludePatterns) && IsMatch(t.Target.Id, request.IncludePatterns)).ToList();
                await TryUpdateProgress(job.Id, $"Noise reduction: Filtered from {originalCount} to {triples.Count} triples.", cancellationToken);
            }

            await TryUpdateProgress(job.Id, $"Writing {triples.Count} triples to graph database...", cancellationToken);

            await graphWriter.UpsertAsync(
                request.RepoName,
                head,
                triples,
                msg => TryUpdateProgress(job.Id, msg, cancellationToken),
                cancellationToken);

            await CompleteJobAsync(job, head, changedFiles.Count, request, cancellationToken);
        }
        catch (Exception ex)
        {
            await FailJobAsync(job, ex, request, cancellationToken);
        }
    }

    private async Task CompleteJobAsync(IndexingJob job, string commit, int count, ReindexRequest req, CancellationToken ct)
    {
        var completedAt = DateTimeOffset.UtcNow;
        try
        {
            await TryUpdateProgress(job.Id, "Finalizing job and updating repo state...", ct);
            await jobs.CompleteJobAsync(job.Id, completedAt, count, "COMPLETED", ct);
            await repoState.UpsertRepoAsync(new RepoState(req.RepoName, req.RemoteUrl, req.Branch, req.SolutionPath, commit, completedAt, "READY", req.IncludePatterns), ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to complete job {job.Id}: {ex.Message}");
        }
    }

    private async Task FailJobAsync(IndexingJob job, Exception ex, ReindexRequest req, CancellationToken ct)
    {
        var completedAt = DateTimeOffset.UtcNow;
        try
        {
            await jobs.FailJobAsync(job.Id, completedAt, ex.Message, ct);
        }
        catch (Exception sex)
        {
            Console.WriteLine($"[ERROR] Failed to record failure for job {job.Id}: {sex.Message}");
        }

        try
        {
            await repoState.UpsertRepoAsync(new RepoState(req.RepoName, req.RemoteUrl, req.Branch, req.SolutionPath, null, null, "FAILED", req.IncludePatterns), ct);
        }
        catch (Exception sex)
        {
            Console.WriteLine($"[ERROR] Failed to update repo state to FAILED for {req.RepoName}: {sex.Message}");
        }
    }

    private async Task TryUpdateProgress(long jobId, string message, CancellationToken ct)
    {
        try
        {
            await jobs.UpdateJobProgressAsync(jobId, message, ct);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not update progress for job {jobId}: {ex.Message}");
        }
    }

    private static bool IsMatch(string id, string[] patterns)
    {
        foreach (var p in patterns)
        {
            if (id.Contains(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}

public sealed record IndexerOptions(string CheckoutRoot);

