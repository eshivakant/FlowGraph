using FlowGraph.Graph;

namespace FlowGraph.Roslyn;

public sealed record RoslynIngestionRequest(
    string RepoName,
    string RepoRootPath,
    string CommitSha,
    string? SolutionPath,
    IReadOnlyList<string> ChangedFiles
);

public interface IRoslynIngestor
{
    Task<IReadOnlyList<GraphTriple>> IngestAsync(RoslynIngestionRequest request, Func<string, Task>? progress, CancellationToken cancellationToken);
}
