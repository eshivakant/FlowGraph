using Microsoft.Extensions.Logging;

namespace FlowGraph.Blobs;

public sealed class FileSystemBlobStore(string rootPath, ILogger<FileSystemBlobStore> logger) : IBlobStore
{
    public async Task WriteAsync(string relativePath, Stream content, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(relativePath);
        logger.LogDebug("Writing blob.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken);
        logger.LogInformation("Blob written.");
    }

    public Task<Stream> ReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        logger.LogDebug("Reading blob.");
        var fullPath = GetFullPath(relativePath);
        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var exists = File.Exists(GetFullPath(relativePath));
        logger.LogDebug("Blob existence check completed: {Exists}.", exists);
        return Task.FromResult(exists);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var fullPath = GetFullPath(relativePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Blob deleted.");
        }

        return Task.CompletedTask;
    }

    private string GetFullPath(string relativePath)
    {
        if (relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Relative path must not contain '..'.", nameof(relativePath));
        }

        return Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
