using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace FlowGraph.Git;

public sealed class GitCliService(ILogger<GitCliService> logger) : IGitService
{
    public async Task<RepoCheckout> EnsureRepoAsync(
        string repoName,
        string remoteUrl,
        string branch,
        string checkoutRoot,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Ensuring local checkout.");
        var localPath = Path.Combine(checkoutRoot, repoName);
        Directory.CreateDirectory(checkoutRoot);

        if (!Directory.Exists(Path.Combine(localPath, ".git")))
        {
            logger.LogInformation("Cloning repository.");
            await RunAsync(checkoutRoot, ["clone", "--no-tags", "--branch", branch, "--single-branch", remoteUrl, repoName], cancellationToken);
        }
        else
        {
            logger.LogInformation("Updating existing repository.");
            await RunAsync(localPath, ["fetch", "--all", "--prune"], cancellationToken);
            await RunAsync(localPath, ["checkout", branch], cancellationToken);
            await RunAsync(localPath, ["pull", "--ff-only"], cancellationToken);
        }

        var head = await RunAsync(localPath, ["rev-parse", "HEAD"], cancellationToken);
        logger.LogInformation("Repository checkout ready.");
        return new RepoCheckout(localPath, head.Trim());
    }

    public async Task<bool> CommitExistsAsync(string localRepoPath, string commitSha, CancellationToken cancellationToken)
    {
        logger.LogDebug("Checking commit existence.");
        var result = await RunAsync(localRepoPath, ["cat-file", "-e", $"{commitSha}^{{commit}}"], cancellationToken, allowNonZeroExit: true);
        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string localRepoPath,
        string fromCommit,
        string toCommit,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Getting changed files.");
        var output = await RunAsync(localRepoPath, ["diff", "--name-only", $"{fromCommit}..{toCommit}"], cancellationToken);
        var files = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        logger.LogInformation("Detected {Count} changed C# files.", files.Length);
        return files;
    }

    private Task<string> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken cancellationToken)
        => RunAsync(workingDir, args, cancellationToken, allowNonZeroExit: false).ContinueWith(t => t.Result.StdOut, cancellationToken);

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string workingDir,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        bool allowNonZeroExit)
    {
        logger.LogDebug("Executing git command.");
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start git process.");

        var stdOutTask = proc.StandardOutput.ReadToEndAsync(cancellationToken);
        var stdErrTask = proc.StandardError.ReadToEndAsync(cancellationToken);

        await proc.WaitForExitAsync(cancellationToken);
        var stdOut = await stdOutTask;
        var stdErr = await stdErrTask;

        if (!allowNonZeroExit && proc.ExitCode != 0)
        {
            logger.LogError("Git command failed with exit code {ExitCode}.", proc.ExitCode);
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({proc.ExitCode}). {stdErr}");
        }

        if (allowNonZeroExit && proc.ExitCode != 0)
        {
            logger.LogWarning("Git command exited non-zero with exit code {ExitCode}.", proc.ExitCode);
        }
        return (proc.ExitCode, stdOut, stdErr);
    }
}
