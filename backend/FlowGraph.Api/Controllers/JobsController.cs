using FlowGraph.State;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController(IIndexJobStore jobs, ILogger<JobsController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IndexingJob>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        var boundedTake = Math.Clamp(take, 1, 500);
        logger.LogInformation("Listing jobs with take={Take}.", boundedTake);
        try
        {
            var results = await jobs.ListJobsAsync(boundedTake, cancellationToken);
            logger.LogInformation("Listed {Count} jobs.", results.Count);
            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list jobs.");
            throw;
        }
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<IndexingJob>> Get(long id, CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting job {JobId}.", id);
        try
        {
            var job = await jobs.GetJobAsync(id, cancellationToken);
            if (job is null)
            {
                logger.LogWarning("Job {JobId} not found.", id);
                return NotFound();
            }

            logger.LogInformation("Retrieved job {JobId} with status {Status}.", id, job.Status);
            return Ok(job);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get job {JobId}.", id);
            throw;
        }
    }
}
