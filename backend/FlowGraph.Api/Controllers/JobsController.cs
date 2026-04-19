using FlowGraph.State;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("jobs")]
public sealed class JobsController(IIndexJobStore jobs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IndexingJob>>> List([FromQuery] int take = 50, CancellationToken cancellationToken = default)
        => Ok(await jobs.ListJobsAsync(Math.Clamp(take, 1, 500), cancellationToken));

    [HttpGet("{id:long}")]
    public async Task<ActionResult<IndexingJob>> Get(long id, CancellationToken cancellationToken)
    {
        var job = await jobs.GetJobAsync(id, cancellationToken);
        return job is null ? NotFound() : Ok(job);
    }
}

