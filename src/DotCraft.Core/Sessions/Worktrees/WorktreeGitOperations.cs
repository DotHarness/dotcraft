using System.Globalization;
using DotCraft.Utilities;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

internal static class WorktreeGitOperations
{
    internal static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan GitWorktreeTimeout = TimeSpan.FromSeconds(120);
    private const string EnvironmentPathConfigKey = "dotcraft.environmentPath";

    /// <summary>
    /// Counts the commits on <paramref name="tip"/> beyond <paramref name="baseHead"/>, or, without a base, the ones no
    /// other branch or remote contains. Returns <c>null</c> when Git cannot tell.
    /// </summary>
    internal static async Task<int?> TryCountCommitsAsync(
        string workingDirectory,
        string tip,
        string? baseHead,
        string branchName,
        CancellationToken ct,
        ILogger? logger)
    {
        string[] args = string.IsNullOrWhiteSpace(baseHead)
            ? ["rev-list", "--count", tip, "--not", $"--exclude=refs/heads/{branchName}", "--branches", "--remotes"]
            : ["rev-list", "--count", $"{baseHead.Trim()}..{tip}"];
        var result = await GitProcessRunner.RunAsync(workingDirectory, args, GitTimeout, ct, logger: logger).ConfigureAwait(false);
        return result.ExitCode == 0
               && int.TryParse(result.StdOut.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? Math.Max(0, count)
            : null;
    }

    internal static async Task<string> ResolveRepositoryRootAsync(
        string sourceWorkspace,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            sourceWorkspace,
            ["rev-parse", "--show-toplevel"],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Source workspace is not a git repository: {TrimGitError(result)}");

        return NormalizeAbsolutePath(result.StdOut.Trim(), "repositoryRoot");
    }

    internal static async Task<string> ResolveRefAsync(
        string repositoryRoot,
        string baseRef,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", baseRef],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ArgumentException($"baseRef '{baseRef}' could not be resolved: {TrimGitError(result)}");

        return result.StdOut.Trim();
    }

    internal static async Task ValidateBranchNameAsync(
        string repositoryRoot,
        string branchName,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            repositoryRoot,
            ["check-ref-format", "--branch", branchName],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ArgumentException($"branchName '{branchName}' is not a valid git branch name: {TrimGitError(result)}");
    }

    internal static Task<bool> BranchExistsAsync(
        string repositoryRoot,
        string branchName,
        CancellationToken ct,
        ILogger? logger) =>
        GitSucceedsAsync(
            repositoryRoot,
            ["rev-parse", "--verify", $"refs/heads/{branchName}"],
            ct,
            logger);

    internal static async Task<string> StashDirtyChangesAsync(
        string worktreePath,
        string worktreeId,
        CancellationToken ct,
        ILogger? logger,
        string? excludedDirectory = null)
    {
        var message = $"dotcraft-worktree-handoff:{worktreeId}:{Guid.NewGuid():N}";
        string[] pathspec = excludedDirectory == null ? [] : ["--", ".", $":(exclude){excludedDirectory}"];
        await RunGitRequiredAsync(
            worktreePath,
            ["stash", "push", "--include-untracked", "--message", message, .. pathspec],
            GitTimeout,
            "Failed to stash worktree changes",
            ct,
            logger).ConfigureAwait(false);

        return await FindStashRefAsync(worktreePath, message, ct, logger).ConfigureAwait(false);
    }

    private static async Task<string> FindStashRefAsync(
        string workingDirectory,
        string message,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            workingDirectory,
            ["stash", "list", "--format=%gd%x00%gs"],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to find worktree handoff stash: {TrimGitError(result)}");

        foreach (var line in result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\0', 2);
            if (parts.Length == 2 && parts[1].Contains(message, StringComparison.Ordinal))
                return parts[0].Trim();
        }

        throw new InvalidOperationException("Failed to find worktree handoff stash after creating it.");
    }

    internal static async Task<IReadOnlyList<GitStatusEntry>> ReadDirtyEntriesAsync(
        string root,
        string dataDirectoryName,
        CancellationToken ct,
        ILogger? logger)
    {
        var statusResult = await GitProcessRunner.RunAsync(
            root,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (statusResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to inspect dirty changes: {TrimGitError(statusResult)}");

        return ParseStatusEntries(statusResult.StdOut)
            .Where(entry => !ShouldSkipDirtyPath(entry.Path, dataDirectoryName))
            .ToList();
    }

    private static IEnumerable<GitStatusEntry> ParseStatusEntries(string output)
    {
        if (string.IsNullOrEmpty(output))
            yield break;

        var parts = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var raw = parts[i];
            if (raw.Length < 4)
                continue;

            var indexStatus = raw[0];
            var workTreeStatus = raw[1];
            var path = raw[3..];
            string? oldPath = null;
            if ((indexStatus == 'R' || indexStatus == 'C') && i + 1 < parts.Length)
                oldPath = parts[++i];

            yield return new GitStatusEntry(
                path,
                oldPath,
                indexStatus == 'D' || workTreeStatus == 'D');
        }
    }

    internal static bool ShouldSkipDirtyPath(string relativePath, string dataDirectoryName)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return string.Equals(normalized, dataDirectoryName, StringComparison.Ordinal)
               || normalized.StartsWith(dataDirectoryName + "/", StringComparison.Ordinal);
    }

    internal static async Task<bool> GitSucceedsAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            workingDirectory,
            args,
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    internal static async Task<string> GitReadAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            workingDirectory,
            args,
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : string.Empty;
    }

    internal static async Task RunGitRequiredAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        TimeSpan timeout,
        string failurePrefix,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            workingDirectory,
            args,
            timeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"{failurePrefix}: {TrimGitError(result)}");
    }

    internal static async Task TryRunGitAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken ct,
        ILogger? logger)
    {
        try
        {
            _ = await GitProcessRunner.RunAsync(
                workingDirectory,
                args,
                GitTimeout,
                ct,
                logger: logger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to run git cleanup command during worktree handoff.");
        }
    }

    internal static async Task<string?> ReadEnvironmentPathAsync(
        string worktreePath,
        CancellationToken ct,
        ILogger? logger)
    {
        if (!Directory.Exists(worktreePath))
            return null;
        var value = await GitReadAsync(
            worktreePath,
            ["config", "--worktree", "--get", EnvironmentPathConfigKey],
            ct,
            logger).ConfigureAwait(false);
        return value.Length == 0 ? null : value;
    }

    internal static async Task WriteEnvironmentPathAsync(
        string worktreePath,
        string? environmentPath,
        CancellationToken ct,
        ILogger? logger)
    {
        string[] args = environmentPath == null
            ? ["config", "--worktree", "--unset", EnvironmentPathConfigKey]
            : ["config", "--worktree", EnvironmentPathConfigKey, environmentPath];
        var result = await GitProcessRunner.RunAsync(worktreePath, args, GitTimeout, ct, logger: logger).ConfigureAwait(false);
        if (result.ExitCode != 0
            && environmentPath != null
            && result.StdErr.Contains("worktreeConfig", StringComparison.OrdinalIgnoreCase)
            && await GitSucceedsAsync(worktreePath, ["config", "extensions.worktreeConfig", "true"], ct, logger).ConfigureAwait(false))
        {
            result = await GitProcessRunner.RunAsync(worktreePath, args, GitTimeout, ct, logger: logger).ConfigureAwait(false);
        }

        if (result.ExitCode != 0 && environmentPath != null)
            logger?.LogWarning("Failed to record the environment of the worktree {Path}: {Error}", worktreePath, TrimGitError(result));
    }

    internal static string NormalizeAbsolutePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{paramName} is required.", paramName);
        return Path.GetFullPath(path);
    }

    private static string TrimGitError(GitProcessRunner.GitResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return value.Trim();
    }

    internal sealed record GitStatusEntry(string Path, string? OldPath, bool Deleted);
}
