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
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            new WorktreeCreateRequest(
                options.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges),
            logger,
            ct).ConfigureAwait(false);
    }

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        WorktreeCreateAndStartOptions options,
        ILogger? logger,
        CancellationToken ct)
    {
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            new WorktreeCreateRequest(
                options.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges),
            logger,
            ct).ConfigureAwait(false);
    }

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        WorktreeHandoffOptions options,
        ILogger? logger,
        CancellationToken ct)
    {
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            new WorktreeCreateRequest(
                sourceThread.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges),
            logger,
            ct).ConfigureAwait(false);
    }

    private static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        WorktreeCreateRequest request,
        ILogger? logger,
        CancellationToken ct)
    {
        var stateWorkspace = NormalizeAbsolutePath(sourceThread.WorkspacePath, nameof(sourceThread.WorkspacePath));
        var sourceWorkspace = NormalizeAbsolutePath(sourceExecutionWorkspace, nameof(sourceExecutionWorkspace));
        var repositoryRoot = await ResolveRepositoryRootAsync(sourceWorkspace, ct, logger).ConfigureAwait(false);
        var baseRef = string.IsNullOrWhiteSpace(request.BaseRef) ? "HEAD" : request.BaseRef.Trim();
        var head = await ResolveRefAsync(repositoryRoot, baseRef, ct, logger).ConfigureAwait(false);
        var branchName = await ResolveBranchNameAsync(repositoryRoot, sourceThread, request, ct, logger).ConfigureAwait(false);
        var worktreePath = ResolveWorktreePath(stateWorkspace, branchName, request.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

        var addResult = await GitProcessRunner.RunAsync(
            repositoryRoot,
            ["worktree", "add", "-b", branchName, worktreePath, baseRef],
            GitWorktreeTimeout,
            ct,
            logger: logger).ConfigureAwait(false);
        if (addResult.ExitCode != 0)
            throw new InvalidOperationException($"Failed to create git worktree: {TrimGitError(addResult)}");

        var handoff = request.CopyDirtyChanges
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

    public static async Task<ThreadWorktreeDirtyHandoffInfo> MoveBranchBackToLocalAndRemoveAsync(
        ThreadWorktreeInfo worktree,
        string targetWorkspace,
        CancellationToken ct,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        var worktreePath = EnsureManagedWorktreePath(worktree);
        if (!Directory.Exists(worktreePath))
        {
            return new ThreadWorktreeDirtyHandoffInfo
            {
                Requested = true,
                Status = WorktreeDirtyHandoffStatuses.Skipped
            };
        }

        var targetRoot = await ResolveRepositoryRootAsync(
            NormalizeAbsolutePath(targetWorkspace, nameof(targetWorkspace)),
            ct,
            logger).ConfigureAwait(false);
        var branchName = string.IsNullOrWhiteSpace(worktree.BranchName)
            ? throw new InvalidOperationException("Worktree branch name is required for local handoff.")
            : worktree.BranchName.Trim();
        var sourceEntries = await ReadDirtyEntriesAsync(worktreePath, ct, logger).ConfigureAwait(false);
        var targetEntries = await ReadDirtyEntriesAsync(targetRoot, ct, logger).ConfigureAwait(false);
        var conflicts = DetectDirtyConflicts(sourceEntries, targetEntries);
        if (conflicts.Count > 0)
        {
            throw new WorktreeHandoffConflictException(
                "Cannot move worktree changes back to local because local has conflicting uncommitted changes.",
                conflicts);
        }

        var stashRef = sourceEntries.Count > 0
            ? await StashDirtyChangesAsync(worktreePath, worktree.Id, ct, logger).ConfigureAwait(false)
            : null;

        var detached = false;
        var localSwitched = false;
        var stashAppliedLocally = false;
        var targetOriginalBranch = await GitReadAsync(
            targetRoot,
            ["branch", "--show-current"],
            ct,
            logger).ConfigureAwait(false);
        var targetOriginalHead = string.IsNullOrWhiteSpace(targetOriginalBranch)
            ? await GitReadAsync(targetRoot, ["rev-parse", "HEAD"], ct, logger).ConfigureAwait(false)
            : string.Empty;
        try
        {
            await RunGitRequiredAsync(
                worktreePath,
                ["switch", "--detach"],
                GitTimeout,
                "Failed to detach worktree branch",
                ct,
                logger).ConfigureAwait(false);
            detached = true;

            await RunGitRequiredAsync(
                targetRoot,
                ["switch", branchName],
                GitTimeout,
                $"Failed to check out branch '{branchName}' locally",
                ct,
                logger).ConfigureAwait(false);
            localSwitched = true;

            if (stashRef != null)
            {
                await RunGitRequiredAsync(
                    targetRoot,
                    ["stash", "apply", stashRef],
                    GitTimeout,
                    "Failed to apply worktree changes locally",
                    ct,
                    logger).ConfigureAwait(false);
                stashAppliedLocally = true;
                await TryRunGitAsync(
                    targetRoot,
                    ["stash", "drop", stashRef],
                    ct,
                    logger).ConfigureAwait(false);
            }

            await RunGitRequiredAsync(
                targetRoot,
                ["worktree", "remove", "--force", worktreePath],
                GitWorktreeTimeout,
                "Failed to remove git worktree",
                ct,
                logger).ConfigureAwait(false);
        }
        catch
        {
            if (localSwitched && !stashAppliedLocally)
            {
                if (!string.IsNullOrWhiteSpace(targetOriginalBranch))
                    await TryRunGitAsync(targetRoot, ["switch", targetOriginalBranch], ct, logger).ConfigureAwait(false);
                else if (!string.IsNullOrWhiteSpace(targetOriginalHead))
                    await TryRunGitAsync(targetRoot, ["switch", "--detach", targetOriginalHead], ct, logger).ConfigureAwait(false);
            }
            if (detached && !stashAppliedLocally)
            {
                await TryRunGitAsync(worktreePath, ["switch", branchName], ct, logger).ConfigureAwait(false);
                if (stashRef != null)
                {
                    await TryRunGitAsync(worktreePath, ["stash", "apply", stashRef], ct, logger).ConfigureAwait(false);
                    await TryRunGitAsync(worktreePath, ["stash", "drop", stashRef], ct, logger).ConfigureAwait(false);
                }
            }
            throw;
        }

        return BuildDirtyHandoffInfo(sourceEntries);
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
        WorktreeCreateRequest request,
        CancellationToken ct,
        ILogger? logger)
    {
        if (!string.IsNullOrWhiteSpace(request.BranchName))
        {
            var requested = request.BranchName.Trim();
            await ValidateBranchNameAsync(repositoryRoot, requested, ct, logger).ConfigureAwait(false);
            return requested;
        }

        var seed = request.DisplayName ?? sourceThread.DisplayName ?? sourceThread.Id;
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
        var copied = 0;
        var deleted = 0;
        foreach (var entry in await ReadDirtyEntriesAsync(sourceRoot, ct, logger).ConfigureAwait(false))
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

    private static ThreadWorktreeDirtyHandoffInfo BuildDirtyHandoffInfo(
        IReadOnlyList<GitStatusEntry> entries)
    {
        var copied = 0;
        var deleted = 0;
        foreach (var entry in entries)
        {
            if (entry.Deleted)
                deleted++;
            else
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

    private static async Task<string> StashDirtyChangesAsync(
        string worktreePath,
        string worktreeId,
        CancellationToken ct,
        ILogger? logger)
    {
        var message = $"dotcraft-worktree-handoff:{worktreeId}:{Guid.NewGuid():N}";
        await RunGitRequiredAsync(
            worktreePath,
            ["stash", "push", "--include-untracked", "--message", message],
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

    private static async Task<IReadOnlyList<GitStatusEntry>> ReadDirtyEntriesAsync(
        string root,
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
            .Where(entry => !ShouldSkipDirtyPath(entry.Path))
            .ToList();
    }

    private static IReadOnlyList<string> DetectDirtyConflicts(
        IReadOnlyList<GitStatusEntry> sourceEntries,
        IReadOnlyList<GitStatusEntry> targetEntries)
    {
        var targetPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targetEntries)
        {
            AddAffectedPath(targetPaths, target.Path);
            if (target.OldPath != null)
                AddAffectedPath(targetPaths, target.OldPath);
        }

        var conflicts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var source in sourceEntries)
        {
            AddConflictIfDirty(targetPaths, conflicts, source.Path);
            if (source.OldPath != null)
                AddConflictIfDirty(targetPaths, conflicts, source.OldPath);
        }

        return conflicts.ToList();
    }

    private static void AddAffectedPath(ISet<string> paths, string path)
    {
        if (!ShouldSkipDirtyPath(path))
            paths.Add(NormalizeGitRelativePath(path));
    }

    private static void AddConflictIfDirty(
        IReadOnlySet<string> targetPaths,
        ISet<string> conflicts,
        string path)
    {
        var normalized = NormalizeGitRelativePath(path);
        if (targetPaths.Contains(normalized))
            conflicts.Add(normalized);
    }

    private static string NormalizeGitRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

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

    private static async Task RunGitRequiredAsync(
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

    private static async Task TryRunGitAsync(
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

    private static string EnsureManagedWorktreePath(ThreadWorktreeInfo worktree)
    {
        var stateWorkspace = NormalizeAbsolutePath(worktree.WorkspacePath, nameof(worktree.WorkspacePath));
        var worktreesRoot = NormalizeAbsolutePath(Path.Combine(stateWorkspace, ".craft", "worktrees"), "worktreesRoot");
        var worktreePath = NormalizeAbsolutePath(worktree.Path, nameof(worktree.Path));
        if (!IsInsideDirectory(worktreePath, worktreesRoot) || string.Equals(worktreePath, worktreesRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Worktree removal is allowed only for registered managed worktrees.");
        return worktreePath;
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

    private sealed record WorktreeCreateRequest(
        string? DisplayName,
        string? BranchName,
        string? BaseRef,
        string? Path,
        bool CopyDirtyChanges);

    private sealed record GitStatusEntry(string Path, string? OldPath, bool Deleted);
}
