using System.Globalization;
using DotCraft.Utilities;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

internal static class ThreadWorktreeManager
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan GitWorktreeTimeout = TimeSpan.FromSeconds(120);

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        WorktreeCreateAndForkOptions options,
        ILogger? logger,
        CancellationToken ct)
    {
        var stateWorkspace = NormalizeAbsolutePath(sourceThread.WorkspacePath, nameof(sourceThread.WorkspacePath));
        var sourceWorkspace = NormalizeAbsolutePath(sourceExecutionWorkspace, nameof(sourceExecutionWorkspace));
        var repositoryRoot = await ResolveRepositoryRootAsync(sourceWorkspace, ct, logger).ConfigureAwait(false);
        var baseRef = string.IsNullOrWhiteSpace(options.BaseRef) ? "HEAD" : options.BaseRef.Trim();
        var head = await ResolveRefAsync(repositoryRoot, baseRef, ct, logger).ConfigureAwait(false);
        var branchName = await ResolveBranchNameAsync(repositoryRoot, sourceThread, options, ct, logger).ConfigureAwait(false);
        var worktreePath = ResolveWorktreePath(stateWorkspace, branchName, options.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

        var addResult = await GitProcessRunner.RunAsync(
            repositoryRoot,
            ["worktree", "add", "-b", branchName, worktreePath, baseRef],
            GitWorktreeTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (addResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to create git worktree: {TrimGitError(addResult)}");

        var handoff = options.CopyDirtyChanges
            ? await CopyDirtyChangesAsync(repositoryRoot, worktreePath, ct, logger).ConfigureAwait(false)
            : new ThreadWorktreeDirtyHandoffInfo
            {
                Requested = false,
                Status = WorktreeDirtyHandoffStatuses.Skipped
            };

        return new ThreadWorktreeInfo
        {
            Id = NewWorktreeId(),
            SourceThreadId = sourceThread.Id,
            WorkspacePath = stateWorkspace,
            SourceWorkspacePath = sourceWorkspace,
            Path = worktreePath,
            BranchName = branchName,
            BaseRef = baseRef,
            Head = head,
            CreatedAt = DateTimeOffset.UtcNow,
            DirtyHandoff = handoff
        };
    }

    public static async Task<ThreadWorktreeStatus> GetStatusAsync(
        string threadId,
        ThreadWorktreeInfo worktree,
        CancellationToken ct,
        ILogger? logger = null)
    {
        var path = worktree.Path;
        var exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        if (!exists)
        {
            return new ThreadWorktreeStatus
            {
                ThreadId = threadId,
                Worktree = worktree,
                Path = path,
                BranchName = worktree.BranchName,
                Head = worktree.Head,
                Exists = false,
                IsGitWorktree = false,
                HasUncommittedChanges = false
            };
        }

        var isGitWorktree = await GitSucceedsAsync(
            path,
            ["rev-parse", "--is-inside-work-tree"],
            ct,
            logger).ConfigureAwait(false);
        if (!isGitWorktree)
        {
            return new ThreadWorktreeStatus
            {
                ThreadId = threadId,
                Worktree = worktree,
                Path = path,
                BranchName = worktree.BranchName,
                Head = worktree.Head,
                Exists = true,
                IsGitWorktree = false,
                HasUncommittedChanges = false
            };
        }

        var branch = await GitReadAsync(path, ["branch", "--show-current"], ct, logger).ConfigureAwait(false);
        var head = await GitReadAsync(path, ["rev-parse", "HEAD"], ct, logger).ConfigureAwait(false);
        var status = await GitReadAsync(path, ["status", "--porcelain=v1"], ct, logger).ConfigureAwait(false);

        return new ThreadWorktreeStatus
        {
            ThreadId = threadId,
            Worktree = worktree,
            Path = path,
            BranchName = string.IsNullOrWhiteSpace(branch) ? worktree.BranchName : branch,
            Head = string.IsNullOrWhiteSpace(head) ? worktree.Head : head,
            Exists = true,
            IsGitWorktree = true,
            HasUncommittedChanges = !string.IsNullOrWhiteSpace(status)
        };
    }

    private static async Task<string> ResolveRepositoryRootAsync(
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

    private static async Task<string> ResolveRefAsync(
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

    private static async Task<string> ResolveBranchNameAsync(
        string repositoryRoot,
        SessionThread sourceThread,
        WorktreeCreateAndForkOptions options,
        CancellationToken ct,
        ILogger? logger)
    {
        if (!string.IsNullOrWhiteSpace(options.BranchName))
        {
            var requested = options.BranchName.Trim();
            await ValidateBranchNameAsync(repositoryRoot, requested, ct, logger).ConfigureAwait(false);
            return requested;
        }

        var seed = options.DisplayName ?? sourceThread.DisplayName ?? sourceThread.Id;
        var slug = Slug(seed);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var candidate = $"dotcraft/{slug}-{suffix}";
            await ValidateBranchNameAsync(repositoryRoot, candidate, ct, logger).ConfigureAwait(false);
            if (!await BranchExistsAsync(repositoryRoot, candidate, ct, logger).ConfigureAwait(false))
                return candidate;
        }

        throw new InvalidOperationException("Failed to allocate a unique worktree branch name.");
    }

    private static async Task ValidateBranchNameAsync(
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

    private static async Task<bool> BranchExistsAsync(
        string repositoryRoot,
        string branchName,
        CancellationToken ct,
        ILogger? logger)
    {
        var result = await GitProcessRunner.RunAsync(
            repositoryRoot,
            ["rev-parse", "--verify", $"refs/heads/{branchName}"],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static string ResolveWorktreePath(string stateWorkspace, string branchName, string? requestedPath)
    {
        var worktreesRoot = NormalizeAbsolutePath(
            Path.Combine(stateWorkspace, ".craft", "worktrees"),
            "worktreesRoot");

        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(worktreesRoot, Slug(branchName.Replace('/', '-')))
            : Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(worktreesRoot, requestedPath);
        var fullPath = NormalizeAbsolutePath(path, "path");

        if (!IsInsideDirectory(fullPath, worktreesRoot))
            throw new ArgumentException("Worktree path must resolve under '<workspace>/.craft/worktrees/'.");
        if (string.Equals(fullPath, worktreesRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Worktree path must be a child path under '<workspace>/.craft/worktrees/'.");
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new InvalidOperationException($"Worktree path '{fullPath}' already exists and is not empty.");
        if (File.Exists(fullPath))
            throw new InvalidOperationException($"Worktree path '{fullPath}' already exists as a file.");

        return fullPath;
    }

    private static async Task<ThreadWorktreeDirtyHandoffInfo> CopyDirtyChangesAsync(
        string sourceRoot,
        string targetRoot,
        CancellationToken ct,
        ILogger? logger)
    {
        var statusResult = await GitProcessRunner.RunAsync(
            sourceRoot,
            ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
            GitTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (statusResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to inspect source dirty changes: {TrimGitError(statusResult)}");

        var copied = 0;
        var deleted = 0;
        foreach (var entry in ParseStatusEntries(statusResult.StdOut))
        {
            ct.ThrowIfCancellationRequested();
            if (ShouldSkipDirtyPath(entry.Path))
                continue;

            if (entry.OldPath != null && !ShouldSkipDirtyPath(entry.OldPath))
            {
                DeleteTargetPath(sourceRoot, targetRoot, entry.OldPath);
                deleted++;
            }

            if (!SourcePathExists(sourceRoot, entry.Path))
            {
                DeleteTargetPath(sourceRoot, targetRoot, entry.Path);
                deleted++;
                continue;
            }

            CopyPath(sourceRoot, targetRoot, entry.Path);
            copied++;
        }

        return new ThreadWorktreeDirtyHandoffInfo
        {
            Requested = true,
            Status = WorktreeDirtyHandoffStatuses.Succeeded,
            CopiedFileCount = copied,
            DeletedFileCount = deleted
        };
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

    private static bool ShouldSkipDirtyPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return string.Equals(normalized, ".craft", StringComparison.Ordinal)
               || normalized.StartsWith(".craft/", StringComparison.Ordinal);
    }

    private static bool SourcePathExists(string sourceRoot, string relativePath)
    {
        var sourcePath = ResolveUnderRoot(sourceRoot, relativePath);
        return File.Exists(sourcePath) || Directory.Exists(sourcePath);
    }

    private static void CopyPath(string sourceRoot, string targetRoot, string relativePath)
    {
        var sourcePath = ResolveUnderRoot(sourceRoot, relativePath);
        var targetPath = ResolveUnderRoot(targetRoot, relativePath);

        if (Directory.Exists(sourcePath))
        {
            foreach (var sourceFile in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceRoot, sourceFile);
                CopyFile(sourceFile, ResolveUnderRoot(targetRoot, rel));
            }

            return;
        }

        if (File.Exists(sourcePath))
            CopyFile(sourcePath, targetPath);
    }

    private static void CopyFile(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void DeleteTargetPath(string sourceRoot, string targetRoot, string relativePath)
    {
        _ = sourceRoot;
        var targetPath = ResolveUnderRoot(targetRoot, relativePath);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
            return;
        }

        if (Directory.Exists(targetPath))
            Directory.Delete(targetPath, recursive: true);
    }

    private static string ResolveUnderRoot(string root, string relativePath)
    {
        var combined = NormalizeAbsolutePath(Path.Combine(root, relativePath), "relativePath");
        var normalizedRoot = NormalizeAbsolutePath(root, "root");
        if (!IsInsideDirectory(combined, normalizedRoot))
            throw new InvalidOperationException($"Git status path '{relativePath}' escapes the repository root.");
        return combined;
    }

    private static async Task<bool> GitSucceedsAsync(
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

    private static async Task<string> GitReadAsync(
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

    private static string NormalizeAbsolutePath(string path, string paramName)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{paramName} is required.", paramName);
        return Path.GetFullPath(path);
    }

    private static bool IsInsideDirectory(string path, string root)
    {
        var fullPath = NormalizeAbsolutePath(path, nameof(path));
        var fullRoot = NormalizeAbsolutePath(root, nameof(root)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Slug(string value)
    {
        var chars = new List<char>(value.Length);
        var previousDash = false;
        foreach (var ch in value.ToLowerInvariant())
        {
            var next = char.IsLetterOrDigit(ch) ? ch : '-';
            if (next == '-')
            {
                if (previousDash)
                    continue;
                previousDash = true;
            }
            else
            {
                previousDash = false;
            }

            chars.Add(next);
            if (chars.Count >= 48)
                break;
        }

        var slug = new string(chars.ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "worktree" : slug;
    }

    private static string NewWorktreeId() =>
        "worktree_" + DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N")[..8];

    private static string TrimGitError(GitProcessRunner.GitResult result)
    {
        var value = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        return value.Trim();
    }

    private sealed record GitStatusEntry(string Path, string? OldPath, bool Deleted);
}
