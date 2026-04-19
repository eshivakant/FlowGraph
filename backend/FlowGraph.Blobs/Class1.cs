namespace FlowGraph.Blobs;

public interface IBlobStore
{
    Task WriteAsync(string relativePath, Stream content, CancellationToken cancellationToken);
    Task<Stream> ReadAsync(string relativePath, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken);
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

