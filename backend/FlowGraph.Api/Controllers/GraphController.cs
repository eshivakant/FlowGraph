using FlowGraph.Graph;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("graph")]
public sealed class GraphController(IGraphQueryService graph) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Search([FromQuery] string? query, [FromQuery] string[]? repos, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Ok(Array.Empty<GraphEntity>());
        }

        return Ok(await graph.SearchAsync(query, repos, Math.Clamp(take, 1, 200), cancellationToken));
    }

    [HttpGet("trace")]
    public async Task<ActionResult<TraceResult>> Trace([FromQuery] string? start, [FromQuery] string[]? repos, [FromQuery] int maxDepth = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return Ok(new TraceResult(null, Array.Empty<GraphTriple>()));
        }

        return Ok(await graph.TraceAsync(start, repos, Math.Clamp(maxDepth, 1, 20), cancellationToken));
    }

    [HttpGet("impact")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Impact([FromQuery] string? change, [FromQuery] string[]? repos, [FromQuery] int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(change))
        {
            return Ok(Array.Empty<GraphEntity>());
        }

        return Ok(await graph.ImpactAsync(change, repos, Math.Clamp(maxDepth, 1, 10), cancellationToken));
    }
}

