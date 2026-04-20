using FlowGraph.Indexer;
using FlowGraph.State;
using FlowGraph.Graph;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("repos")]
public sealed class ReposController(
    IRepoIndexer indexer, 
    IRepoStateStore repoState,
    IIndexJobStore jobStore,
    IGraphConnectionRouter graphRouter,
    ILogger<ReposController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RepoState>>> List(CancellationToken cancellationToken)
    {
        logger.LogInformation("Listing repositories.");
        try
        {
            var repos = await repoState.ListReposAsync(cancellationToken);
            logger.LogInformation("Listed {Count} repositories.", repos.Count);
            return Ok(repos);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list repositories.");
            throw;
        }
    }

    public sealed record ReindexBody(string RemoteUrl, string? Branch, string? Mode, string? SolutionPath, string[]? IncludePatterns, string? GraphConnection);

    [HttpPost("{repo}/reindex")]
    public async Task<ActionResult<object>> Reindex(string repo, [FromBody] ReindexBody body, CancellationToken cancellationToken)
    {
        var mode = string.Equals(body.Mode, "full", StringComparison.OrdinalIgnoreCase) ? IndexMode.Full : IndexMode.Incremental;
        logger.LogInformation("Reindex requested with mode {Mode}.", mode);
        try
        {
            var job = await indexer.ReindexAsync(
                new ReindexRequest(
                    RepoName: repo,
                    RemoteUrl: body.RemoteUrl,
                    Branch: body.Branch ?? "main",
                    Mode: mode,
                    SolutionPath: body.SolutionPath,
                    IncludePatterns: body.IncludePatterns,
                    GraphConnection: body.GraphConnection),
                cancellationToken);

            logger.LogInformation("Reindex job {JobId} created.", job.Id);
            return Ok(new { jobId = job.Id, status = job.Status });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue reindex.");
            throw;
        }
    }

    [HttpDelete("{repo}")]
    public async Task<ActionResult> Delete(string repo, [FromQuery] string? connection, CancellationToken cancellationToken)
    {
        logger.LogInformation("Deleting repository data.");
        try
        {
            await graphRouter.GetWriter(connection).DeleteRepoAsync(repo, cancellationToken);
            await jobStore.DeleteJobsForRepoAsync(repo, cancellationToken);
            await repoState.DeleteRepoAsync(repo, cancellationToken);
            logger.LogInformation("Deleted repository data.");
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete repository data.");
            throw;
        }
    }

    [HttpPost("wipe-graph")]
    public async Task<ActionResult> WipeGraph([FromQuery] string? connection, CancellationToken cancellationToken)
    {
        logger.LogWarning("Graph wipe requested.");
        try
        {
            await graphRouter.GetWriter(connection).WipeAsync(cancellationToken);
            logger.LogWarning("Graph wipe completed.");
            return Ok();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph wipe failed.");
            throw;
        }
    }
}
