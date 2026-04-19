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
    IGraphWriter graphWriter) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RepoState>>> List(CancellationToken cancellationToken)
        => Ok(await repoState.ListReposAsync(cancellationToken));

    public sealed record ReindexBody(string RemoteUrl, string? Branch, string? Mode, string? SolutionPath, string[]? IncludePatterns);

    [HttpPost("{repo}/reindex")]
    public async Task<ActionResult<object>> Reindex(string repo, [FromBody] ReindexBody body, CancellationToken cancellationToken)
    {
        var mode = string.Equals(body.Mode, "full", StringComparison.OrdinalIgnoreCase) ? IndexMode.Full : IndexMode.Incremental;
        var job = await indexer.ReindexAsync(
            new ReindexRequest(
                RepoName: repo,
                RemoteUrl: body.RemoteUrl,
                Branch: body.Branch ?? "main",
                Mode: mode,
                SolutionPath: body.SolutionPath,
                IncludePatterns: body.IncludePatterns),
            cancellationToken);

        return Ok(new { jobId = job.Id, status = job.Status });
    }

    [HttpDelete("{repo}")]
    public async Task<ActionResult> Delete(string repo, CancellationToken cancellationToken)
    {
        await graphWriter.DeleteRepoAsync(repo, cancellationToken);
        await jobStore.DeleteJobsForRepoAsync(repo, cancellationToken);
        await repoState.DeleteRepoAsync(repo, cancellationToken);
        return NoContent();
    }

    [HttpPost("wipe-graph")]
    public async Task<ActionResult> WipeGraph(CancellationToken cancellationToken)
    {
        await graphWriter.WipeAsync(cancellationToken);
        return Ok();
    }
}

