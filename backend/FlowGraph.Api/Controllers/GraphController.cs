using FlowGraph.Graph;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("graph")]
public sealed class GraphController(IGraphQueryService graph, ILogger<GraphController> logger) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Search([FromQuery] string? query, [FromQuery] string[]? repos, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogInformation("Search requested with empty query.");
            return Ok(Array.Empty<GraphEntity>());
        }

        var boundedTake = Math.Clamp(take, 1, 200);
        logger.LogInformation("Searching graph for query '{Query}' with take={Take}.", query, boundedTake);
        try
        {
            var results = await graph.SearchAsync(query, repos, boundedTake, cancellationToken);
            logger.LogInformation("Search returned {Count} results for query '{Query}'.", results.Count, query);
            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph search failed for query '{Query}'.", query);
            throw;
        }
    }

    [HttpGet("trace")]
    public async Task<ActionResult<TraceResult>> Trace([FromQuery] string? start, [FromQuery] string[]? repos, [FromQuery] int maxDepth = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            logger.LogInformation("Trace requested with empty start node.");
            return Ok(new TraceResult(null, Array.Empty<GraphTriple>()));
        }

        var boundedDepth = Math.Clamp(maxDepth, 1, 20);
        logger.LogInformation("Tracing graph from '{Start}' with maxDepth={MaxDepth}.", start, boundedDepth);
        try
        {
            var result = await graph.TraceAsync(start, repos, boundedDepth, cancellationToken);
            logger.LogInformation("Trace completed for '{Start}' with {TripleCount} triples.", start, result.Triples.Count);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph trace failed for start '{Start}'.", start);
            throw;
        }
    }

    [HttpGet("impact")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Impact([FromQuery] string? change, [FromQuery] string[]? repos, [FromQuery] int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(change))
        {
            logger.LogInformation("Impact requested with empty change id.");
            return Ok(Array.Empty<GraphEntity>());
        }

        var boundedDepth = Math.Clamp(maxDepth, 1, 10);
        logger.LogInformation("Calculating impact for '{Change}' with maxDepth={MaxDepth}.", change, boundedDepth);
        try
        {
            var results = await graph.ImpactAsync(change, repos, boundedDepth, cancellationToken);
            logger.LogInformation("Impact returned {Count} entities for '{Change}'.", results.Count, change);
            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impact analysis failed for '{Change}'.", change);
            throw;
        }
    }
}
