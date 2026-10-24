using System.Globalization;
using static DotCraft.Sessions.WorktreeGitOperations;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

internal static class ThreadWorktreeManager
{
    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        string dataPath,
        WorktreeCreateAndForkOptions options,
        string branchPrefix,
        ILogger? logger,
        CancellationToken ct)
    {
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            dataPath,
            new WorktreeCreateRequest(
                options.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges ? DirtyChangeHandling.Copy : DirtyChangeHandling.None,
                branchPrefix),
            logger,
            ct).ConfigureAwait(false);
    }

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        string dataPath,
        WorktreeCreateAndStartOptions options,
        string branchPrefix,
        ILogger? logger,
        CancellationToken ct)
    {
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            dataPath,
            new WorktreeCreateRequest(
                options.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges ? DirtyChangeHandling.Copy : DirtyChangeHandling.None,
                branchPrefix),
            logger,
            ct).ConfigureAwait(false);
    }

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        string dataPath,
        WorktreeHandoffOptions options,
        string branchPrefix,
        ILogger? logger,
        CancellationToken ct)
    {
        return await CreateAsync(
            sourceThread,
            sourceExecutionWorkspace,
            dataPath,
            new WorktreeCreateRequest(
                sourceThread.DisplayName,
                options.BranchName,
                options.BaseRef,
                options.Path,
                options.CopyDirtyChanges ? DirtyChangeHandling.Move : DirtyChangeHandling.None,
                branchPrefix,
                options.Progress),
            logger,
            ct).ConfigureAwait(false);
    }

    public static async Task<ThreadWorktreeInfo> EnsureAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        string dataPath,
        WorktreeEnsureOptions options,
        ThreadWorktreeInfo? existing,
        ILogger? logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceThread);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BranchName))
            throw new ArgumentException("branchName is required.", nameof(options));

        var stateWorkspace = NormalizeAbsolutePath(sourceThread.WorkspacePath, nameof(sourceThread.WorkspacePath));
        dataPath = NormalizeAbsolutePath(dataPath, nameof(dataPath));
        var sourceWorkspace = NormalizeAbsolutePath(sourceExecutionWorkspace, nameof(sourceExecutionWorkspace));
        var repositoryRoot = await ResolveRepositoryRootAsync(sourceWorkspace, ct, logger).ConfigureAwait(false);
        var baseRef = string.IsNullOrWhiteSpace(options.BaseRef) ? "HEAD" : options.BaseRef.Trim();
        var branchName = options.BranchName.Trim();
        await ValidateBranchNameAsync(repositoryRoot, branchName, ct, logger).ConfigureAwait(false);
        var baseHead = await ResolveRefAsync(repositoryRoot, baseRef, ct, logger).ConfigureAwait(false);

        var worktreePath = ResolveWorktreePath(dataPath, branchName, options.Path, allowExisting: true);
        if (existing != null
            && string.Equals(NormalizeAbsolutePath(existing.Path, nameof(existing.Path)), worktreePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.BranchName, branchName, StringComparison.Ordinal))
        {
            var status = await GetStatusAsync(sourceThread.Id, existing, ct, logger).ConfigureAwait(false);
            if (status.Exists
                && status.IsGitWorktree
                && string.Equals(status.BranchName, branchName, StringComparison.Ordinal))
            {
                return existing with
                {
                    BaseHead = string.IsNullOrWhiteSpace(existing.BaseHead) ? baseHead : existing.BaseHead,
                    Head = string.IsNullOrWhiteSpace(status.Head) ? existing.Head : status.Head,
                    OwnerKind = string.IsNullOrWhiteSpace(existing.OwnerKind) ? options.OwnerKind : existing.OwnerKind,
                    OwnerId = string.IsNullOrWhiteSpace(existing.OwnerId) ? options.OwnerId : existing.OwnerId
                };
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
        await TryRunGitAsync(repositoryRoot, ["worktree", "prune"], ct, logger).ConfigureAwait(false);

        if (Directory.Exists(worktreePath))
        {
            var isGitWorktree = await GitSucceedsAsync(
                worktreePath,
                ["rev-parse", "--is-inside-work-tree"],
                ct,
                logger).ConfigureAwait(false);
            if (isGitWorktree)
            {
                var currentBranch = await GitReadAsync(
                    worktreePath,
                    ["branch", "--show-current"],
                    ct,
                    logger).ConfigureAwait(false);
                if (string.Equals(currentBranch, branchName, StringComparison.Ordinal))
                {
                    var currentHead = await GitReadAsync(
                        worktreePath,
                        ["rev-parse", "HEAD"],
                        ct,
                        logger).ConfigureAwait(false);
                    return BuildEnsuredInfo(
                        sourceThread,
                        stateWorkspace,
                        sourceWorkspace,
                        worktreePath,
                        branchName,
                        baseRef,
                        baseHead,
                        string.IsNullOrWhiteSpace(currentHead) ? existing?.Head ?? string.Empty : currentHead,
                        options.OwnerKind,
                        options.OwnerId,
                        existing);
                }

                await TryRunGitAsync(
                    repositoryRoot,
                    ["worktree", "remove", "--force", worktreePath],
                    ct,
                    logger).ConfigureAwait(false);
            }

            if (Directory.Exists(worktreePath))
                Directory.Delete(worktreePath, recursive: true);
        }
        else if (File.Exists(worktreePath))
        {
            File.Delete(worktreePath);
        }

        var branchExists = await BranchExistsAsync(repositoryRoot, branchName, ct, logger).ConfigureAwait(false);
        var head = branchExists ? await ResolveRefAsync(
            repositoryRoot,
            $"refs/heads/{branchName}",
            ct,
            logger).ConfigureAwait(false) : baseHead;

        var addArgs = branchExists
            ? new[] { "worktree", "add", worktreePath, branchName }
            : new[] { "worktree", "add", "-b", branchName, worktreePath, baseRef };
        await RunGitRequiredAsync(
            repositoryRoot,
            addArgs,
            GitWorktreeTimeout,
            "Failed to ensure git worktree",
            ct,
            logger).ConfigureAwait(false);

        return BuildEnsuredInfo(
            sourceThread,
            stateWorkspace,
            sourceWorkspace,
            worktreePath,
            branchName,
            baseRef,
            baseHead,
            head,
            options.OwnerKind,
            options.OwnerId,
            existing);
    }

    public static async Task<ThreadWorktreeInfo> CreateAsync(
        SessionThread sourceThread,
        string sourceExecutionWorkspace,
        string dataPath,
        WorktreeCreateRequest request,
        ILogger? logger,
        CancellationToken ct)
    {
        var stateWorkspace = NormalizeAbsolutePath(sourceThread.WorkspacePath, nameof(sourceThread.WorkspacePath));
        dataPath = NormalizeAbsolutePath(dataPath, nameof(dataPath));
        var sourceWorkspace = NormalizeAbsolutePath(sourceExecutionWorkspace, nameof(sourceExecutionWorkspace));
        var repositoryRoot = await ResolveRepositoryRootAsync(sourceWorkspace, ct, logger).ConfigureAwait(false);
        var baseRef = string.IsNullOrWhiteSpace(request.BaseRef) ? "HEAD" : request.BaseRef.Trim();
        var head = await ResolveRefAsync(repositoryRoot, baseRef, ct, logger).ConfigureAwait(false);
        var branchName = await ResolveBranchNameAsync(repositoryRoot, sourceThread, request, ct, logger).ConfigureAwait(false);
        var worktreePath = ResolveWorktreePath(dataPath, branchName, request.Path);

        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);

        try
        {
            await RunStepAsync(request.Progress, WorktreeHandoffSteps.CreateWorktree, () => RunGitRequiredAsync(
                repositoryRoot,
                ["worktree", "add", "-b", branchName, worktreePath, baseRef],
                GitWorktreeTimeout,
                "Failed to create git worktree",
                ct,
                logger)).ConfigureAwait(false);
        }
        catch when (ct.IsCancellationRequested)
        {
            await DiscardNewWorktreeAsync(repositoryRoot, worktreePath, branchName, logger).ConfigureAwait(false);
            throw;
        }

        ThreadWorktreeDirtyHandoffInfo handoff;
        try
        {
            var dataDirectoryName = Path.GetFileName(dataPath);
            handoff = await RunStepAsync(request.ReportCopyStep ? request.Progress : null, WorktreeCreateSteps.CopyFiles, async () =>
            {
                await CopyIncludedFilesAsync(repositoryRoot, worktreePath, ct, logger).ConfigureAwait(false);
                return request.DirtyChanges switch
                {
                    DirtyChangeHandling.Copy => await CopyDirtyChangesAsync(repositoryRoot, worktreePath, dataDirectoryName, ct, logger).ConfigureAwait(false),
                    DirtyChangeHandling.Move => await MoveDirtyChangesAsync(repositoryRoot, worktreePath, dataDirectoryName, request.Progress, ct, logger).ConfigureAwait(false),
                    _ => new ThreadWorktreeDirtyHandoffInfo { Requested = false, Status = WorktreeDirtyHandoffStatuses.Skipped }
                };
            }).ConfigureAwait(false);
        }
        catch
        {
            await RunStepAsync(request.ReportCopyStep ? null : request.Progress, WorktreeHandoffSteps.Rollback,
                () => DiscardNewWorktreeAsync(repositoryRoot, worktreePath, branchName, logger)).ConfigureAwait(false);
            throw;
        }

        return new ThreadWorktreeInfo
        {
            Id = NewWorktreeId(),
            SourceThreadId = sourceThread.Id,
            WorkspacePath = stateWorkspace,
            SourceWorkspacePath = sourceWorkspace,
            Path = worktreePath,
            BranchName = branchName,
            BaseRef = baseRef,
            BaseHead = head,
            Head = head,
            CreatedAt = DateTimeOffset.UtcNow,
            DirtyHandoff = handoff
        };
    }

    public static async Task<ThreadWorktreeDirtyHandoffInfo> MoveBranchBackToLocalAndRemoveAsync(
        ThreadWorktreeInfo worktree,
        string targetWorkspace,
        string dataPath,
        Action<string, string>? progress,
        CancellationToken ct,
        ILogger? logger = null,
        Func<Task>? beforeChanges = null)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        dataPath = NormalizeAbsolutePath(dataPath, nameof(dataPath));
        var worktreePath = EnsureManagedWorktreePath(worktree, dataPath);
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
        var dataDirectoryName = Path.GetFileName(dataPath);
        var sourceEntries = await ReadDirtyEntriesAsync(worktreePath, dataDirectoryName, ct, logger).ConfigureAwait(false);
        var targetEntries = await ReadDirtyEntriesAsync(targetRoot, dataDirectoryName, ct, logger).ConfigureAwait(false);
        if (targetEntries.Count > 0)
        {
            throw new WorktreeHandoffConflictException(
                "Commit or stash the local changes before moving the worktree back.",
                [.. targetEntries.Select(static entry => entry.Path.Replace('\\', '/')).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }

        if (beforeChanges != null)
            await beforeChanges().ConfigureAwait(false);

        EnsureFolderNotInUse(worktreePath);

        var stashRef = sourceEntries.Count > 0
            ? await RunStepAsync(progress, WorktreeHandoffSteps.StashSourceChanges,
                () => StashDirtyChangesAsync(worktreePath, worktree.Id, ct, logger)).ConfigureAwait(false)
            : Skip<string>(progress, WorktreeHandoffSteps.StashSourceChanges);

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
            await RunStepAsync(progress, WorktreeHandoffSteps.DetachWorktreeBranch, () => RunGitRequiredAsync(
                worktreePath,
                ["switch", "--detach"],
                GitTimeout,
                "Failed to detach worktree branch",
                ct,
                logger)).ConfigureAwait(false);
            detached = true;

            await RunStepAsync(progress, WorktreeHandoffSteps.CheckoutLocalBranch, () => RunGitRequiredAsync(
                targetRoot,
                ["switch", branchName],
                GitTimeout,
                $"Failed to check out branch '{branchName}' locally",
                ct,
                logger)).ConfigureAwait(false);
            localSwitched = true;

            if (stashRef != null)
            {
                await RunStepAsync(progress, WorktreeHandoffSteps.ApplyChanges, () => RunGitRequiredAsync(
                    targetRoot,
                    ["stash", "apply", "--index", stashRef],
                    GitTimeout,
                    "Failed to apply worktree changes locally",
                    ct,
                    logger)).ConfigureAwait(false);
                stashAppliedLocally = true;
                await TryRunGitAsync(
                    targetRoot,
                    ["stash", "drop", stashRef],
                    ct,
                    logger).ConfigureAwait(false);
            }
            else
            {
                Skip<string>(progress, WorktreeHandoffSteps.ApplyChanges);
            }
        }
        catch
        {
            progress?.Invoke(WorktreeHandoffSteps.Rollback, WorktreeHandoffStepStatuses.Started);
            if (localSwitched && !stashAppliedLocally)
            {
                await TryRunGitAsync(targetRoot, ["reset", "--hard"], CancellationToken.None, logger).ConfigureAwait(false);
                await TryRunGitAsync(targetRoot, ["clean", "-fd", "-e", $"/{dataDirectoryName}/"], CancellationToken.None, logger).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(targetOriginalBranch))
                    await TryRunGitAsync(targetRoot, ["switch", targetOriginalBranch], ct, logger).ConfigureAwait(false);
                else if (!string.IsNullOrWhiteSpace(targetOriginalHead))
                    await TryRunGitAsync(targetRoot, ["switch", "--detach", targetOriginalHead], ct, logger).ConfigureAwait(false);
            }
            if (detached && !stashAppliedLocally)
            {
                await TryRunGitAsync(worktreePath, ["switch", branchName], ct, logger).ConfigureAwait(false);
                if (stashRef != null
                    && await GitSucceedsAsync(worktreePath, ["stash", "apply", "--index", stashRef], CancellationToken.None, logger).ConfigureAwait(false))
                {
                    await TryRunGitAsync(worktreePath, ["stash", "drop", stashRef], CancellationToken.None, logger).ConfigureAwait(false);
                }
            }
            progress?.Invoke(WorktreeHandoffSteps.Rollback, WorktreeHandoffStepStatuses.Completed);
            throw;
        }

        await TryRunGitAsync(targetRoot, ["worktree", "remove", "--force", worktreePath], CancellationToken.None, logger).ConfigureAwait(false);
        if (Directory.Exists(worktreePath))
        {
            try
            {
                Directory.Delete(worktreePath, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(ex, "Left the worktree folder {Path} on disk after handing its thread back.", worktreePath);
            }
        }

        await TryRunGitAsync(targetRoot, ["worktree", "prune"], CancellationToken.None, logger).ConfigureAwait(false);
        return BuildDirtyHandoffInfo(sourceEntries);
    }

    private static void EnsureFolderNotInUse(string worktreePath)
    {
        var probePath = $"{worktreePath}.{Guid.NewGuid():N}";
        try
        {
            Directory.Move(worktreePath, probePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new WorktreeInUseException(worktreePath);
        }

        Directory.Move(probePath, worktreePath);
    }

    public static async Task<ThreadWorktreeInfo> MoveIntoExistingAsync(
        SessionThread thread,
        string sourceExecutionWorkspace,
        string dataPath,
        ThreadWorktreeStatus target,
        bool moveChanges,
        Action<string, string>? progress,
        ILogger? logger,
        CancellationToken ct)
    {
        dataPath = NormalizeAbsolutePath(dataPath, nameof(dataPath));
        var worktreePath = EnsureManagedWorktreePath(target.Worktree, dataPath);
        var sourceRoot = await ResolveRepositoryRootAsync(
            NormalizeAbsolutePath(sourceExecutionWorkspace, nameof(sourceExecutionWorkspace)),
            ct,
            logger).ConfigureAwait(false);
        progress?.Invoke(WorktreeHandoffSteps.CreateWorktree, WorktreeHandoffStepStatuses.Skipped);
        progress?.Invoke(WorktreeHandoffSteps.ReuseWorktree, WorktreeHandoffStepStatuses.Completed);

        var dataDirectoryName = Path.GetFileName(dataPath);
        var targetStash = target.HasUncommittedChanges
            ? await RunStepAsync(progress, WorktreeHandoffSteps.StashTargetChanges,
                () => StashDirtyChangesAsync(worktreePath, "target", ct, logger)).ConfigureAwait(false)
            : Skip<string>(progress, WorktreeHandoffSteps.StashTargetChanges);
        ThreadWorktreeDirtyHandoffInfo handoff;
        try
        {
            handoff = moveChanges
                ? await MoveDirtyChangesAsync(sourceRoot, worktreePath, dataDirectoryName, progress, ct, logger).ConfigureAwait(false)
                : new ThreadWorktreeDirtyHandoffInfo { Requested = false, Status = WorktreeDirtyHandoffStatuses.Skipped };
        }
        catch
        {
            if (targetStash != null
                && await GitSucceedsAsync(worktreePath, ["stash", "apply", "--index", targetStash], CancellationToken.None, logger).ConfigureAwait(false))
            {
                await TryRunGitAsync(worktreePath, ["stash", "drop", targetStash], CancellationToken.None, logger).ConfigureAwait(false);
            }
            throw;
        }

        var head = await GitReadAsync(worktreePath, ["rev-parse", "HEAD"], ct, logger).ConfigureAwait(false);
        return target.Worktree with
        {
            Id = NewWorktreeId(),
            SourceThreadId = thread.Id,
            Head = head,
            DirtyHandoff = handoff with { TargetStashed = targetStash != null }
        };
    }

    private static async Task RunStepAsync(Action<string, string>? progress, string step, Func<Task> action) =>
        await RunStepAsync(progress, step, async () =>
        {
            await action().ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);

    private static async Task<T> RunStepAsync<T>(Action<string, string>? progress, string step, Func<Task<T>> action)
    {
        progress?.Invoke(step, WorktreeHandoffStepStatuses.Started);
        try
        {
            var result = await action().ConfigureAwait(false);
            progress?.Invoke(step, WorktreeHandoffStepStatuses.Completed);
            return result;
        }
        catch
        {
            progress?.Invoke(step, WorktreeHandoffStepStatuses.Failed);
            throw;
        }
    }

    private static T? Skip<T>(Action<string, string>? progress, string step) where T : class
    {
        progress?.Invoke(step, WorktreeHandoffStepStatuses.Skipped);
        return null;
    }

    public static async Task RemoveManagedWorktreeAndBranchAsync(
        ThreadWorktreeInfo worktree,
        string dataPath,
        bool deleteBranch,
        CancellationToken ct,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(worktree);
        dataPath = NormalizeAbsolutePath(dataPath, nameof(dataPath));
        var worktreePath = EnsureManagedWorktreePath(worktree, dataPath);
        string repositoryRoot;
        try
        {
            repositoryRoot = await ResolveRepositoryRootAsync(SourceWorkspace(worktree), ct, logger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Skipping managed worktree git removal because the source workspace is not a git repository.");
            if (Directory.Exists(worktreePath))
            {
                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
                catch (Exception deleteEx)
                {
                    logger?.LogWarning(deleteEx, "Failed to delete managed worktree directory {Path}.", worktreePath);
                }
            }

            return;
        }

        if (Directory.Exists(worktreePath))
        {
            await TryRunGitAsync(
                repositoryRoot,
                ["worktree", "remove", "--force", worktreePath],
                ct,
                logger).ConfigureAwait(false);

            if (Directory.Exists(worktreePath))
            {
                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to delete managed worktree directory {Path}.", worktreePath);
                }
            }
        }

        await TryRunGitAsync(repositoryRoot, ["worktree", "prune"], ct, logger).ConfigureAwait(false);

        if (deleteBranch && !string.IsNullOrWhiteSpace(worktree.BranchName))
        {
            await TryRunGitAsync(
                repositoryRoot,
                ["branch", "-D", worktree.BranchName],
                ct,
                logger).ConfigureAwait(false);
        }
    }

    public static async Task CheckOutAgainAsync(
        ThreadWorktreeInfo worktree,
        string dataPath,
        CancellationToken ct,
        ILogger? logger = null)
    {
        var worktreePath = EnsureManagedWorktreePath(worktree, NormalizeAbsolutePath(dataPath, nameof(dataPath)));
        if (Directory.Exists(worktreePath))
            return;

        var repositoryRoot = await ResolveRepositoryRootAsync(SourceWorkspace(worktree), ct, logger).ConfigureAwait(false);
        await TryRunGitAsync(repositoryRoot, ["worktree", "prune"], ct, logger).ConfigureAwait(false);
        Directory.CreateDirectory(Path.GetDirectoryName(worktreePath)!);
        await RunGitRequiredAsync(
            repositoryRoot,
            ["worktree", "add", worktreePath, worktree.BranchName],
            GitWorktreeTimeout,
            $"Failed to check out branch '{worktree.BranchName}' again",
            ct,
            logger).ConfigureAwait(false);
        await CopyIncludedFilesAsync(repositoryRoot, worktreePath, ct, logger).ConfigureAwait(false);
    }

    public static async Task RemoveCheckoutAsync(
        ThreadWorktreeInfo worktree,
        string dataPath,
        CancellationToken ct,
        ILogger? logger = null) =>
        await RemoveManagedWorktreeAndBranchAsync(worktree, dataPath, deleteBranch: false, ct, logger).ConfigureAwait(false);

    public static async Task<IReadOnlyList<(string Path, string BranchName)>> ListCheckoutsAsync(
        string stateWorkspace,
        string dataPath,
        CancellationToken ct,
        ILogger? logger = null)
    {
        var worktreesRoot = NormalizeAbsolutePath(Path.Combine(dataPath, "worktrees"), "worktreesRoot");
        string repositoryRoot;
        try
        {
            repositoryRoot = await ResolveRepositoryRootAsync(stateWorkspace, ct, logger).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return [];
        }

        var output = await GitReadAsync(repositoryRoot, ["worktree", "list", "--porcelain"], ct, logger).ConfigureAwait(false);
        var checkouts = new List<(string, string)>();
        string? path = null;
        foreach (var line in output.Split('\n'))
        {
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                path = NormalizeAbsolutePath(line["worktree ".Length..].Trim(), "path");
            }
            else if (line.StartsWith("branch refs/heads/", StringComparison.Ordinal)
                     && path != null
                     && IsInsideDirectory(path, worktreesRoot)
                     && !string.Equals(path, worktreesRoot, StringComparison.OrdinalIgnoreCase))
            {
                checkouts.Add((path, line["branch refs/heads/".Length..].Trim()));
            }
        }

        return checkouts;
    }

    public static async Task<ThreadWorktreeStatus> GetStatusAsync(
        string? threadId,
        ThreadWorktreeInfo worktree,
        CancellationToken ct,
        ILogger? logger = null)
    {
        var path = worktree.Path;
        var exists = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        var baseHead = string.IsNullOrWhiteSpace(worktree.BaseHead) ? worktree.BaseRef : worktree.BaseHead;
        var isGitWorktree = exists && await GitSucceedsAsync(
            path,
            ["rev-parse", "--is-inside-work-tree"],
            ct,
            logger).ConfigureAwait(false);
        if (!isGitWorktree)
        {
            var commits = await TryCountBranchCommitsInRepositoryAsync(worktree, baseHead, ct, logger).ConfigureAwait(false);
            return new ThreadWorktreeStatus
            {
                ThreadId = threadId,
                Worktree = worktree,
                Path = path,
                BranchName = worktree.BranchName,
                Head = worktree.Head,
                Exists = exists,
                IsGitWorktree = false,
                HasUncommittedChanges = false,
                HasCommitsAheadOfBase = commits is not 0,
                AheadCount = commits ?? 0
            };
        }

        var branch = await GitReadAsync(path, ["branch", "--show-current"], ct, logger).ConfigureAwait(false);
        var head = await GitReadAsync(path, ["rev-parse", "HEAD"], ct, logger).ConfigureAwait(false);
        var status = await GitReadAsync(path, ["status", "--porcelain=v1"], ct, logger).ConfigureAwait(false);
        var aheadCount = await TryCountCommitsAsync(path, "HEAD", baseHead, branch, ct, logger).ConfigureAwait(false);

        return new ThreadWorktreeStatus
        {
            ThreadId = threadId,
            Worktree = worktree,
            Path = path,
            BranchName = string.IsNullOrWhiteSpace(branch) ? worktree.BranchName : branch,
            Head = string.IsNullOrWhiteSpace(head) ? worktree.Head : head,
            Exists = true,
            IsGitWorktree = true,
            HasUncommittedChanges = !string.IsNullOrWhiteSpace(status),
            HasCommitsAheadOfBase = aheadCount is not 0,
            AheadCount = aheadCount ?? 0
        };
    }

    private static async Task<int?> TryCountBranchCommitsInRepositoryAsync(
        ThreadWorktreeInfo worktree,
        string? baseHead,
        CancellationToken ct,
        ILogger? logger)
    {
        string repositoryRoot;
        try
        {
            repositoryRoot = await ResolveRepositoryRootAsync(SourceWorkspace(worktree), ct, logger).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (!await BranchExistsAsync(repositoryRoot, worktree.BranchName, ct, logger).ConfigureAwait(false))
            return 0;
        return await TryCountCommitsAsync(
            repositoryRoot,
            $"refs/heads/{worktree.BranchName}",
            baseHead,
            worktree.BranchName,
            ct,
            logger).ConfigureAwait(false);
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
            if (await BranchExistsAsync(repositoryRoot, requested, ct, logger).ConfigureAwait(false))
                throw new ArgumentException($"Branch '{requested}' already exists.");
            return requested;
        }

        var seed = request.DisplayName ?? sourceThread.DisplayName ?? sourceThread.Id;
        var slug = Slug(seed);
        var prefix = string.IsNullOrWhiteSpace(request.BranchPrefix) ? "dotcraft" : request.BranchPrefix.Trim().TrimEnd('/');
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..8];
            var candidate = $"{prefix}/{slug}-{suffix}";
            await ValidateBranchNameAsync(repositoryRoot, candidate, ct, logger).ConfigureAwait(false);
            if (!await BranchExistsAsync(repositoryRoot, candidate, ct, logger).ConfigureAwait(false))
                return candidate;
        }

        throw new InvalidOperationException("Failed to allocate a unique worktree branch name.");
    }

    private static ThreadWorktreeInfo BuildEnsuredInfo(
        SessionThread sourceThread,
        string stateWorkspace,
        string sourceWorkspace,
        string worktreePath,
        string branchName,
        string baseRef,
        string baseHead,
        string head,
        string? ownerKind,
        string? ownerId,
        ThreadWorktreeInfo? existing) =>
        new()
        {
            Id = string.IsNullOrWhiteSpace(existing?.Id) ? NewWorktreeId() : existing!.Id,
            SourceThreadId = sourceThread.Id,
            WorkspacePath = stateWorkspace,
            SourceWorkspacePath = sourceWorkspace,
            Path = worktreePath,
            BranchName = branchName,
            BaseRef = string.IsNullOrWhiteSpace(existing?.BaseRef) ? baseRef : existing!.BaseRef,
            BaseHead = string.IsNullOrWhiteSpace(existing?.BaseHead) ? baseHead : existing!.BaseHead,
            Head = head,
            OwnerKind = string.IsNullOrWhiteSpace(existing?.OwnerKind) ? ownerKind : existing!.OwnerKind,
            OwnerId = string.IsNullOrWhiteSpace(existing?.OwnerId) ? ownerId : existing!.OwnerId,
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            DirtyHandoff = new ThreadWorktreeDirtyHandoffInfo
            {
                Requested = false,
                Status = WorktreeDirtyHandoffStatuses.Skipped
            }
        };

    private static string ResolveWorktreePath(
        string dataPath,
        string branchName,
        string? requestedPath,
        bool allowExisting = false)
    {
        var worktreesRoot = NormalizeAbsolutePath(
            Path.Combine(dataPath, "worktrees"),
            "worktreesRoot");

        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(worktreesRoot, Slug(branchName.Replace('/', '-')))
            : Path.IsPathRooted(requestedPath)
                ? requestedPath
                : Path.Combine(worktreesRoot, requestedPath);
        var fullPath = NormalizeAbsolutePath(path, "path");

        if (!IsInsideDirectory(fullPath, worktreesRoot))
            throw new ArgumentException("Worktree path must resolve under the configured data directory's 'worktrees' directory.");
        if (string.Equals(fullPath, worktreesRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Worktree path must be a child of the configured data directory's 'worktrees' directory.");
        if (!allowExisting && Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new InvalidOperationException($"Worktree path '{fullPath}' already exists and is not empty.");
        if (!allowExisting && File.Exists(fullPath))
            throw new InvalidOperationException($"Worktree path '{fullPath}' already exists as a file.");

        return fullPath;
    }

    private static async Task<ThreadWorktreeDirtyHandoffInfo> CopyDirtyChangesAsync(
        string sourceRoot,
        string targetRoot,
        string dataDirectoryName,
        CancellationToken ct,
        ILogger? logger)
    {
        var copied = 0;
        var deleted = 0;
        foreach (var entry in await ReadDirtyEntriesAsync(sourceRoot, dataDirectoryName, ct, logger).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (ShouldSkipDirtyPath(entry.Path, dataDirectoryName))
                continue;

            if (entry.OldPath != null && !ShouldSkipDirtyPath(entry.OldPath, dataDirectoryName))
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

    private static async Task<ThreadWorktreeDirtyHandoffInfo> MoveDirtyChangesAsync(
        string sourceRoot,
        string targetRoot,
        string dataDirectoryName,
        Action<string, string>? progress,
        CancellationToken ct,
        ILogger? logger)
    {
        var entries = await ReadDirtyEntriesAsync(sourceRoot, dataDirectoryName, ct, logger).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            Skip<string>(progress, WorktreeHandoffSteps.StashSourceChanges);
            Skip<string>(progress, WorktreeHandoffSteps.ApplyChanges);
            return BuildDirtyHandoffInfo(entries);
        }

        var dataDirectoryIgnored = await GitSucceedsAsync(sourceRoot, ["check-ignore", "-q", dataDirectoryName], ct, logger).ConfigureAwait(false);
        var stashRef = await RunStepAsync(progress, WorktreeHandoffSteps.StashSourceChanges, () => StashDirtyChangesAsync(
            sourceRoot,
            Guid.NewGuid().ToString("N")[..8],
            ct,
            logger,
            excludedDirectory: dataDirectoryIgnored ? null : dataDirectoryName)).ConfigureAwait(false);
        try
        {
            await RunStepAsync(progress, WorktreeHandoffSteps.ApplyChanges, () => RunGitRequiredAsync(
                targetRoot,
                ["stash", "apply", "--index", stashRef],
                GitTimeout,
                "Failed to apply uncommitted changes in the worktree",
                ct,
                logger)).ConfigureAwait(false);
        }
        catch
        {
            progress?.Invoke(WorktreeHandoffSteps.Rollback, WorktreeHandoffStepStatuses.Started);
            await TryRunGitAsync(targetRoot, ["reset", "--hard"], CancellationToken.None, logger).ConfigureAwait(false);
            await TryRunGitAsync(targetRoot, ["clean", "-fd"], CancellationToken.None, logger).ConfigureAwait(false);
            await RunGitRequiredAsync(
                sourceRoot,
                ["stash", "apply", "--index", stashRef],
                GitTimeout,
                "Failed to restore uncommitted changes in the source checkout; they are kept in the stash",
                CancellationToken.None,
                logger).ConfigureAwait(false);
            await TryRunGitAsync(sourceRoot, ["stash", "drop", stashRef], CancellationToken.None, logger).ConfigureAwait(false);
            throw;
        }

        await TryRunGitAsync(sourceRoot, ["stash", "drop", stashRef], ct, logger).ConfigureAwait(false);
        return BuildDirtyHandoffInfo(entries);
    }

    private static async Task CopyIncludedFilesAsync(
        string repositoryRoot,
        string worktreePath,
        CancellationToken ct,
        ILogger? logger)
    {
        var includeFile = Path.Combine(repositoryRoot, ".worktreeinclude");
        if (!File.Exists(includeFile))
            return;

        var candidates = (await GitReadAsync(
                repositoryRoot,
                ["ls-files", "--others", "--ignored", $"--exclude-from={includeFile}", "-z"],
                ct,
                logger).ConfigureAwait(false))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var batch in candidates.Chunk(100))
        {
            var ignored = await GitReadAsync(
                    repositoryRoot,
                    ["-c", "core.quotePath=false", "check-ignore", "--", .. batch],
                    ct,
                    logger).ConfigureAwait(false);
            foreach (var path in ignored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                CopyPath(repositoryRoot, worktreePath, path);
        }
    }

    private static async Task DiscardNewWorktreeAsync(
        string repositoryRoot,
        string worktreePath,
        string branchName,
        ILogger? logger)
    {
        await TryRunGitAsync(repositoryRoot, ["worktree", "remove", "--force", worktreePath], CancellationToken.None, logger).ConfigureAwait(false);
        if (Directory.Exists(worktreePath))
        {
            try
            {
                Directory.Delete(worktreePath, recursive: true);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to delete worktree directory {Path} after a failed creation.", worktreePath);
            }
        }

        await TryRunGitAsync(repositoryRoot, ["worktree", "prune"], CancellationToken.None, logger).ConfigureAwait(false);
        await TryRunGitAsync(repositoryRoot, ["branch", "-D", branchName], CancellationToken.None, logger).ConfigureAwait(false);
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

    private static bool IsInsideDirectory(string path, string root)
    {
        var fullPath = NormalizeAbsolutePath(path, nameof(path));
        var fullRoot = NormalizeAbsolutePath(root, nameof(root)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.StartsWith(fullRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SourceWorkspace(ThreadWorktreeInfo worktree) =>
        NormalizeAbsolutePath(
            Directory.Exists(worktree.SourceWorkspacePath) ? worktree.SourceWorkspacePath : worktree.WorkspacePath,
            nameof(worktree.WorkspacePath));

    private static string EnsureManagedWorktreePath(ThreadWorktreeInfo worktree, string dataPath)
    {
        var worktreesRoot = NormalizeAbsolutePath(Path.Combine(dataPath, "worktrees"), "worktreesRoot");
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
}

internal enum DirtyChangeHandling
{
    None,
    Copy,
    Move
}

internal sealed record WorktreeCreateRequest(
    string? DisplayName,
    string? BranchName,
    string? BaseRef,
    string? Path,
    DirtyChangeHandling DirtyChanges,
    string BranchPrefix,
    Action<string, string>? Progress = null,
    bool ReportCopyStep = false);
