using System.Diagnostics;

namespace FlowGraph.Git;

public sealed class GitCliService : IGitService
{
    public async Task<RepoCheckout> EnsureRepoAsync(
        string repoName,
        string remoteUrl,
        string branch,
        string checkoutRoot,
        CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(checkoutRoot, repoName);
        Directory.CreateDirectory(checkoutRoot);

        if (!Directory.Exists(Path.Combine(localPath, ".git")))
        {
            await RunAsync(checkoutRoot, ["clone", "--no-tags", "--branch", branch, "--single-branch", remoteUrl, repoName], cancellationToken);
        }
        else
        {
            await RunAsync(localPath, ["fetch", "--all", "--prune"], cancellationToken);
            await RunAsync(localPath, ["checkout", branch], cancellationToken);
            await RunAsync(localPath, ["pull", "--ff-only"], cancellationToken);
        }

        var head = await RunAsync(localPath, ["rev-parse", "HEAD"], cancellationToken);
        return new RepoCheckout(localPath, head.Trim());
    }

    public async Task<bool> CommitExistsAsync(string localRepoPath, string commitSha, CancellationToken cancellationToken)
    {
        var result = await RunAsync(localRepoPath, ["cat-file", "-e", $"{commitSha}^{{commit}}"], cancellationToken, allowNonZeroExit: true);
        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<string>> GetChangedFilesAsync(
        string localRepoPath,
        string fromCommit,
        string toCommit,
        CancellationToken cancellationToken)
    {
        var output = await RunAsync(localRepoPath, ["diff", "--name-only", $"{fromCommit}..{toCommit}"], cancellationToken);
        var files = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return files;
    }

    private static Task<string> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken cancellationToken)
        => RunAsync(workingDir, args, cancellationToken, allowNonZeroExit: false).ContinueWith(t => t.Result.StdOut, cancellationToken);

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string workingDir,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken,
        bool allowNonZeroExit)
    {
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
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({proc.ExitCode}). {stdErr}");
        }

        return (proc.ExitCode, stdOut, stdErr);
    }
}

