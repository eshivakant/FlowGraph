using FlowGraph.Graph;
using Microsoft.AspNetCore.Mvc;

namespace FlowGraph.Api.Controllers;

[ApiController]
[Route("graph")]
public sealed class GraphController(IGraphConnectionRouter graphRouter, ILogger<GraphController> logger) : ControllerBase
{
    [HttpGet("connections")]
    public ActionResult<IReadOnlyList<GraphConnectionInfo>> Connections()
    {
        return Ok(graphRouter.ListConnections());
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Search([FromQuery] string? query, [FromQuery] string[]? repos, [FromQuery] string? connection, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            logger.LogInformation("Search requested with empty query.");
            return Ok(Array.Empty<GraphEntity>());
        }

        var graph = graphRouter.GetQueryService(connection);
        var boundedTake = Math.Clamp(take, 1, 200);
        logger.LogInformation("Searching graph with take={Take}.", boundedTake);
        try
        {
            var results = await graph.SearchAsync(query, repos, boundedTake, cancellationToken);
            logger.LogInformation("Search returned {Count} results.", results.Count);
            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph search failed.");
            throw;
        }
    }

    [HttpGet("trace")]
    public async Task<ActionResult<TraceResult>> Trace([FromQuery] string? start, [FromQuery] string[]? repos, [FromQuery] string? connection, [FromQuery] int maxDepth = 5, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            logger.LogInformation("Trace requested with empty start node.");
            return Ok(new TraceResult(null, Array.Empty<GraphTriple>()));
        }

        var graph = graphRouter.GetQueryService(connection);
        var boundedDepth = Math.Clamp(maxDepth, 1, 20);
        logger.LogInformation("Tracing graph with maxDepth={MaxDepth}.", boundedDepth);
        try
        {
            var result = await graph.TraceAsync(start, repos, boundedDepth, cancellationToken);
            logger.LogInformation("Trace completed with {TripleCount} triples.", result.Triples.Count);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph trace failed.");
            throw;
        }
    }

    [HttpGet("impact")]
    public async Task<ActionResult<IReadOnlyList<GraphEntity>>> Impact([FromQuery] string? change, [FromQuery] string[]? repos, [FromQuery] string? connection, [FromQuery] int maxDepth = 3, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(change))
        {
            logger.LogInformation("Impact requested with empty change id.");
            return Ok(Array.Empty<GraphEntity>());
        }

        var graph = graphRouter.GetQueryService(connection);
        var boundedDepth = Math.Clamp(maxDepth, 1, 10);
        logger.LogInformation("Calculating impact with maxDepth={MaxDepth}.", boundedDepth);
        try
        {
            var results = await graph.ImpactAsync(change, repos, boundedDepth, cancellationToken);
            logger.LogInformation("Impact returned {Count} entities.", results.Count);
            return Ok(results);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Impact analysis failed.");
            throw;
        }
    }
}
