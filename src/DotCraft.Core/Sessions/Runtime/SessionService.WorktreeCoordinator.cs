using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private string WorktreeBranchPrefix =>
        (_appConfigMonitor?.Current ?? agentFactory.RuntimeContext.Config).Worktrees.BranchPrefix;

    private sealed class WorktreeCoordinator(SessionService owner)
    {
        private readonly SemaphoreSlim _moveLock = new(1, 1);

        public async Task<WorktreeCreateAndForkResult> CreateAndForkAsync(
            WorktreeCreateAndForkOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var sourceThreadId = NormalizeRequiredThreadId(options.SourceThreadId);
            var source = await owner.GetOrLoadThreadAsync(sourceThreadId, ct);
            ThrowIfWorktreeIdentityMovesStateWorkspace(source, options.Identity);

            var sourceExecutionWorkspace = ResolveEffectiveWorkspacePath(source);
            var worktree = await ThreadWorktreeManager.CreateAsync(
                source,
                sourceExecutionWorkspace,
                owner.DataPath,
                options,
                owner.WorktreeBranchPrefix,
                owner.Logger,
                ct);
            if (source.Worktree is { } sourceWorktree)
                worktree = worktree with { SourceWorkspacePath = sourceWorktree.SourceWorkspacePath };

            var config = options.Config != null
                ? ThreadConfigurationCloner.Clone(options.Config)
                : source.Configuration != null
                    ? ThreadConfigurationCloner.Clone(source.Configuration)
                    : new ThreadConfiguration();
            config.ExecutionWorkspaceOverride = worktree.Path;

            var identity = options.Identity == null
                ? null
                : options.Identity with { WorkspacePath = source.WorkspacePath };

            SessionThread thread;
            try
            {
                thread = await owner.ForkThreadAsync(
                    source.Id,
                    new ThreadForkOptions
                    {
                        ForkPoint = options.ForkPoint,
                        Identity = identity,
                        Config = config,
                        DisplayName = options.DisplayName,
                        Worktree = worktree
                    },
                    ct);
            }
            catch
            {
                await DiscardAsync(worktree);
                throw;
            }

            return new WorktreeCreateAndForkResult
            {
                Thread = thread,
                Worktree = worktree
            };
        }

        public async Task<WorktreeCreateAndStartResult> CreateAndStartAsync(
            WorktreeCreateAndStartOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var identity = options.Identity;
            if (string.IsNullOrWhiteSpace(identity.WorkspacePath))
                throw new ArgumentException("identity.workspacePath is required.");

            var capturedConfig = owner.CaptureThreadConfigurationForNewThread(options.Config);
            var threadId = SessionIdGenerator.NewThreadId();
            var now = DateTimeOffset.UtcNow;
            var thread = new SessionThread
            {
                Id = threadId,
                WorkspacePath = identity.WorkspacePath,
                UserId = identity.UserId,
                OriginChannel = identity.ChannelName,
                ChannelContext = identity.ChannelContext,
                Status = ThreadStatus.Active,
                CreatedAt = now,
                LastActiveAt = now,
                HistoryMode = options.HistoryMode,
                Configuration = capturedConfig,
                DisplayName = options.DisplayName,
                Source = options.Source ?? ThreadSource.User()
            };

            if (identity.ChannelContext != null)
                thread.Metadata["channelContext"] = identity.ChannelContext;

            var sourceExecutionWorkspace = ResolveLocalWorkspacePath(thread);
            var worktree = await ThreadWorktreeManager.CreateAsync(
                thread,
                sourceExecutionWorkspace,
                owner.DataPath,
                options,
                owner.WorktreeBranchPrefix,
                owner.Logger,
                ct);

            capturedConfig.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;

            owner._runtimeRegistry.SetThread(thread);
            owner._runtimeRegistry.ClearPendingPermanentDeletion(thread.Id);
            var broker = owner.GetOrCreateBroker(thread.Id);

            try
            {
                using (await owner.AcquireThreadAgentLockAsync(thread.Id, ct))
                    owner.SetThreadAgent(thread.Id, await owner.BuildAgentForThreadAsync(thread, ct));

                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }
            catch
            {
                if (owner._runtimeRegistry.TryRemove(thread.Id, out var removed))
                    await removed.DisposeAsync();
                await DiscardAsync(worktree);
                throw;
            }

            await owner.SaveContextUsageSnapshotAsync(thread.Id, 0, ct);

            broker.PublishThreadEvent(SessionEventType.ThreadCreated, thread);
            owner.ThreadCreatedForBroadcast?.Invoke(thread);

            return new WorktreeCreateAndStartResult
            {
                Thread = thread,
                Worktree = worktree
            };
        }

        public async Task<WorktreeHandoffResult> HandoffAsync(
            WorktreeHandoffOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var threadId = NormalizeRequiredThreadId(options.ThreadId);
            var mode = NormalizeWorktreeHandoffMode(options.Mode);

            using (await owner.Gate.AcquireAsync(threadId, ct))
            using (await LockMovesAsync(ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                ThrowIfThreadCannotHandoffWorktree(thread);
                owner.ThrowIfThreadMaintenanceActive(thread.Id);

                if (string.Equals(mode, WorktreeHandoffModes.Worktree, StringComparison.Ordinal))
                {
                    if (thread.Worktree != null)
                    {
                        return new WorktreeHandoffResult
                        {
                            Thread = thread,
                            Mode = WorktreeHandoffModes.Worktree,
                            Worktree = thread.Worktree
                        };
                    }

                    var sourceExecutionWorkspace = ResolveEffectiveWorkspacePath(thread);
                    var target = await FindUnboundAsync(options.Path, ct);
                    ThreadWorktreeInfo worktree;
                    if (target != null)
                    {
                        worktree = await ThreadWorktreeManager.MoveIntoExistingAsync(
                            thread,
                            sourceExecutionWorkspace,
                            owner.DataPath,
                            target,
                            options.CopyDirtyChanges,
                            options.Progress,
                            owner.Logger,
                            ct);
                    }
                    else
                    {
                        options.Progress?.Invoke(WorktreeHandoffSteps.ReuseWorktree, WorktreeHandoffStepStatuses.Skipped);
                        options.Progress?.Invoke(WorktreeHandoffSteps.StashTargetChanges, WorktreeHandoffStepStatuses.Skipped);
                        worktree = await ThreadWorktreeManager.CreateAsync(
                            thread,
                            sourceExecutionWorkspace,
                            owner.DataPath,
                            options,
                            owner.WorktreeBranchPrefix,
                            owner.Logger,
                            ct);
                    }

                    thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                    thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
                    thread.Worktree = worktree;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;

                    await MoveThreadAsync(thread, options.Progress, ct);
                    return new WorktreeHandoffResult
                    {
                        Thread = thread,
                        Mode = WorktreeHandoffModes.Worktree,
                        Worktree = worktree,
                        DirtyHandoff = worktree.DirtyHandoff
                    };
                }

                if (thread.Worktree == null)
                {
                    thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                    thread.Configuration.ExecutionWorkspaceOverride = null;
                    await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                    owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                    return new WorktreeHandoffResult
                    {
                        Thread = thread,
                        Mode = WorktreeHandoffModes.Local
                    };
                }

                var currentWorktree = thread.Worktree;
                var localWorkspace = ResolveLocalWorkspacePath(thread);
                var dirtyHandoff = await ThreadWorktreeManager.MoveBranchBackToLocalAndRemoveAsync(
                    currentWorktree,
                    localWorkspace,
                    owner.DataPath,
                    options.Progress,
                    ct,
                    owner.Logger);

                thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                thread.Configuration.ExecutionWorkspaceOverride = null;
                thread.Worktree = null;
                thread.LastActiveAt = DateTimeOffset.UtcNow;

                await MoveThreadAsync(thread, options.Progress, ct);
                return new WorktreeHandoffResult
                {
                    Thread = thread,
                    Mode = WorktreeHandoffModes.Local,
                    DirtyHandoff = dirtyHandoff
                };
            }
        }

        public async Task<WorktreeEnsureResult> EnsureAsync(
            WorktreeEnsureOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var threadId = NormalizeRequiredThreadId(options.ThreadId);
            if (string.IsNullOrWhiteSpace(options.BranchName))
                throw new ArgumentException("branchName is required.", nameof(options));

            using (await owner.Gate.AcquireAsync(threadId, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                var sourceWorkspace = ResolveLocalWorkspacePath(thread);
                var existing = thread.Worktree;
                var worktree = await ThreadWorktreeManager.EnsureAsync(
                    thread,
                    sourceWorkspace,
                    owner.DataPath,
                    options,
                    existing,
                    owner.Logger,
                    ct);

                thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                var reused = existing != null
                             && string.Equals(existing.Path, worktree.Path, StringComparison.OrdinalIgnoreCase)
                             && string.Equals(existing.BranchName, worktree.BranchName, StringComparison.Ordinal);
                var changed = !reused
                              || !string.Equals(existing?.BaseHead, worktree.BaseHead, StringComparison.OrdinalIgnoreCase)
                              || !string.Equals(existing?.Head, worktree.Head, StringComparison.OrdinalIgnoreCase)
                              || !string.Equals(existing?.OwnerKind, worktree.OwnerKind, StringComparison.Ordinal)
                              || !string.Equals(existing?.OwnerId, worktree.OwnerId, StringComparison.Ordinal)
                              || !string.Equals(thread.Configuration.ExecutionWorkspaceOverride, worktree.Path, StringComparison.OrdinalIgnoreCase);

                thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
                thread.Worktree = worktree;
                if (changed)
                {
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                    owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                }

                return new WorktreeEnsureResult
                {
                    Thread = thread,
                    Worktree = worktree,
                    Reused = reused
                };
            }
        }

        public async Task<SessionThread> ConfigureExecutionWorkspaceAsync(
            ThreadExecutionWorkspaceOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var threadId = NormalizeRequiredThreadId(options.ThreadId);
            using (await owner.Gate.AcquireAsync(threadId, ct))
            {
                var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                var nextExecutionWorkspace = string.IsNullOrWhiteSpace(options.ExecutionWorkspaceOverride)
                    ? null
                    : Path.GetFullPath(options.ExecutionWorkspaceOverride);
                var changed = !string.Equals(
                                  thread.Configuration.ExecutionWorkspaceOverride,
                                  nextExecutionWorkspace,
                                  StringComparison.OrdinalIgnoreCase)
                              || (options.ClearWorktree && thread.Worktree != null);

                thread.Configuration.ExecutionWorkspaceOverride = nextExecutionWorkspace;
                if (options.ClearWorktree)
                    thread.Worktree = null;

                if (changed)
                {
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                    owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                }

                return thread;
            }
        }

        public async Task RemoveAsync(WorktreeRemoveOptions options, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var threadId = string.IsNullOrWhiteSpace(options.ThreadId) ? null : options.ThreadId.Trim();
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                using (await owner.Gate.AcquireAsync(threadId, ct))
                using (await LockMovesAsync(ct))
                {
                    var thread = await TryGetThreadAsync(threadId, ct);
                    var worktree = ResolveRemovalWorktree(options, thread);
                    if (worktree != null)
                    {
                        await ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(
                            worktree,
                            owner.DataPath,
                            options.DeleteBranch,
                            ct,
                            owner.Logger);
                    }

                    if (thread != null)
                    {
                        thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                        thread.Configuration.ExecutionWorkspaceOverride = null;
                        thread.Worktree = null;
                        thread.LastActiveAt = DateTimeOffset.UtcNow;
                        await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                        owner.ThreadUpdatedForBroadcast?.Invoke(thread);
                    }
                }

                return;
            }

            var fallback = ResolveRemovalWorktree(options, thread: null);
            if (fallback != null)
            {
                await ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(
                    fallback,
                    owner.DataPath,
                    options.DeleteBranch,
                    ct,
                    owner.Logger);
            }
        }

        public async Task<IReadOnlyList<ThreadWorktreeStatus>> ListAsync(CancellationToken ct)
        {
            var summaries = await owner.Persistence.LoadIndexAsync(ct);
            var byThreadId = new Dictionary<string, ThreadWorktreeInfo>(StringComparer.Ordinal);
            foreach (var summary in summaries)
            {
                if (summary.Worktree == null)
                    continue;
                byThreadId[summary.Id] = summary.Worktree;
            }

            foreach (var thread in owner._runtimeRegistry.Values.Select(runtime => runtime.Thread))
            {
                if (thread.Ephemeral || thread.Worktree == null)
                    continue;
                byThreadId[thread.Id] = thread.Worktree;
            }

            var statuses = new List<ThreadWorktreeStatus>(byThreadId.Count);
            foreach (var (threadId, worktree) in byThreadId)
            {
                statuses.Add(await ThreadWorktreeManager.GetStatusAsync(threadId, worktree, ct, owner.Logger));
            }

            var boundPaths = new HashSet<string>(
                byThreadId.Values.Select(static worktree => Path.GetFullPath(worktree.Path)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var (path, branchName) in await ThreadWorktreeManager.ListCheckoutsAsync(
                         StateWorkspacePath,
                         owner.DataPath,
                         ct,
                         owner.Logger))
            {
                if (boundPaths.Contains(path))
                    continue;
                var unbound = new ThreadWorktreeInfo
                {
                    WorkspacePath = StateWorkspacePath,
                    SourceWorkspacePath = StateWorkspacePath,
                    Path = path,
                    BranchName = branchName,
                    CreatedAt = Directory.GetCreationTimeUtc(path)
                };
                statuses.Add(await ThreadWorktreeManager.GetStatusAsync(null, unbound, ct, owner.Logger));
            }

            return statuses
                .OrderByDescending(status => status.Worktree.CreatedAt)
                .ToList();
        }

        public async Task<ThreadWorktreeStatus> GetStatusAsync(
            string threadId,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(NormalizeRequiredThreadId(threadId), ct);
            if (thread.Worktree == null)
                throw new InvalidOperationException($"Thread '{thread.Id}' is not bound to a DotCraft worktree.");

            return await ThreadWorktreeManager.GetStatusAsync(thread.Id, thread.Worktree, ct, owner.Logger);
        }

        public async Task<ThreadWorktreeInfo> EnterAsync(
            string threadId,
            string? name,
            string? path,
            ToolLocation location,
            CancellationToken ct)
        {
            using (await LockMovesAsync(ct))
                return await EnterCoreAsync(threadId, name, path, location, ct);
        }

        public async Task<WorktreeExitResult> ExitAsync(
            string threadId,
            bool remove,
            bool discardChanges,
            ToolLocation location,
            CancellationToken ct)
        {
            using (await LockMovesAsync(ct))
                return await ExitCoreAsync(threadId, remove, discardChanges, location, ct);
        }

        private async Task<IDisposable> LockMovesAsync(CancellationToken ct)
        {
            await _moveLock.WaitAsync(ct);
            return new SemaphoreSlimReleaser(_moveLock);
        }

        private async Task<ThreadWorktreeInfo> EnterCoreAsync(
            string threadId,
            string? name,
            string? path,
            ToolLocation location,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Worktree != null)
                throw new InvalidOperationException($"Already working in the worktree on branch '{thread.Worktree.BranchName}'.");

            ThreadWorktreeInfo worktree;
            if (string.IsNullOrWhiteSpace(path))
            {
                worktree = await ThreadWorktreeManager.CreateAsync(
                    thread,
                    ResolveEffectiveWorkspacePath(thread),
                    owner.DataPath,
                    new WorktreeCreateRequest(
                        string.IsNullOrWhiteSpace(name) ? thread.DisplayName : name,
                        BranchName: null,
                        BaseRef: null,
                        Path: null,
                        DirtyChangeHandling.None,
                        owner.WorktreeBranchPrefix),
                    owner.Logger,
                    ct);
            }
            else
            {
                worktree = await ResolveUnboundAsync(thread, path, ct);
            }

            thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
            thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;
            await CommitInTurnMoveAsync(thread, location, ct);
            return worktree;
        }

        private async Task<WorktreeExitResult> ExitCoreAsync(
            string threadId,
            bool remove,
            bool discardChanges,
            ToolLocation location,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            var worktree = thread.Worktree
                ?? throw new InvalidOperationException("This conversation is not in a worktree.");

            if (remove)
            {
                var status = await ThreadWorktreeManager.GetStatusAsync(thread.Id, worktree, ct, owner.Logger);
                if (!discardChanges && (status.HasUncommittedChanges || status.HasCommitsAheadOfBase))
                    return WorktreeExitResult.Refused(status);
                if (await HasRunningTerminalInAsync(thread.Id, worktree.Path, ct))
                    throw new InvalidOperationException("A background command is still running in the worktree. Stop it first.");
            }

            thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
            thread.Configuration.ExecutionWorkspaceOverride = null;
            thread.Worktree = null;
            await CommitInTurnMoveAsync(thread, location, ct);
            if (remove)
                await ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(worktree, owner.DataPath, deleteBranch: true, ct, owner.Logger);
            return WorktreeExitResult.Exited(worktree);
        }

        public async Task RemoveByPathAsync(string path, bool discardChanges, CancellationToken ct)
        {
            using var moves = await LockMovesAsync(ct);
            var fullPath = Path.GetFullPath(path);
            var status = (await ListAsync(ct)).FirstOrDefault(entry =>
                    string.Equals(Path.GetFullPath(entry.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"'{path}' is not a managed worktree.");

            SessionThread? thread = null;
            if (status.ThreadId != null)
            {
                thread = await TryGetThreadAsync(status.ThreadId, ct);
                if (thread is { Status: ThreadStatus.Active })
                    throw new InvalidOperationException("The worktree belongs to an active conversation. Move the conversation back to the local checkout first.");
            }

            if (!discardChanges && (status.HasUncommittedChanges || status.HasCommitsAheadOfBase))
                throw new InvalidOperationException("The worktree has uncommitted changes or new commits. Pass discardChanges to remove it anyway.");

            await ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(status.Worktree, owner.DataPath, deleteBranch: true, ct, owner.Logger);
            if (thread != null)
            {
                thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                thread.Configuration.ExecutionWorkspaceOverride = null;
                thread.Worktree = null;
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
                owner.ThreadUpdatedForBroadcast?.Invoke(thread);
            }
        }

        public async Task ReleaseOnArchiveAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Worktree is not { } worktree)
                return;
            await BestEffortAsync(async () =>
            {
                var status = await ThreadWorktreeManager.GetStatusAsync(thread.Id, worktree, ct, owner.Logger);
                if (status.Exists && status.IsGitWorktree && !status.HasUncommittedChanges)
                    await ThreadWorktreeManager.RemoveCheckoutAsync(worktree, owner.DataPath, ct, owner.Logger);
            }, thread.Id);
        }

        public async Task ReleaseOnDeleteAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Worktree is not { } worktree)
                return;
            await BestEffortAsync(async () =>
            {
                var status = await ThreadWorktreeManager.GetStatusAsync(thread.Id, worktree, ct, owner.Logger);
                if (status.Exists && status.HasUncommittedChanges)
                    return;
                await ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(
                    worktree,
                    owner.DataPath,
                    deleteBranch: !status.HasCommitsAheadOfBase,
                    ct,
                    owner.Logger);
            }, thread.Id);
        }

        public async Task RestoreCheckoutAsync(SessionThread thread, CancellationToken ct)
        {
            if (thread.Worktree is { } worktree && !Directory.Exists(worktree.Path))
                await ThreadWorktreeManager.CheckOutAgainAsync(worktree, owner.DataPath, ct, owner.Logger);
        }

        private async Task MoveThreadAsync(SessionThread thread, Action<string, string>? progress, CancellationToken ct)
        {
            progress?.Invoke(WorktreeHandoffSteps.MoveThread, WorktreeHandoffStepStatuses.Started);
            await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
            owner.ThreadUpdatedForBroadcast?.Invoke(thread);
            progress?.Invoke(WorktreeHandoffSteps.MoveThread, WorktreeHandoffStepStatuses.Completed);
        }

        private async Task<ThreadWorktreeStatus?> FindUnboundAsync(string? path, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var fullPath = Path.GetFullPath(path);
            var status = (await ListAsync(ct)).FirstOrDefault(entry =>
                entry is { Exists: true, IsGitWorktree: true }
                && string.Equals(Path.GetFullPath(entry.Path), fullPath, StringComparison.OrdinalIgnoreCase));
            if (status?.ThreadId != null)
                throw new InvalidOperationException("Another conversation is working in that worktree.");
            return status;
        }

        private async Task<bool> HasRunningTerminalInAsync(string threadId, string worktreePath, CancellationToken ct)
        {
            if (owner.BackgroundTerminalService is not { } terminals)
                return false;
            var root = Path.GetFullPath(worktreePath);
            return (await terminals.ListAsync(threadId, ct)).Any(terminal =>
                terminal.Status == BackgroundTerminalStatus.Running
                && Path.GetFullPath(terminal.WorkingDirectory).StartsWith(root, StringComparison.OrdinalIgnoreCase));
        }

        private string StateWorkspacePath => owner.AgentFactory.RuntimeContext.WorkspacePath;

        private async Task CommitInTurnMoveAsync(SessionThread thread, ToolLocation location, CancellationToken ct)
        {
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            location.MoveTo(ThreadWorkspaceResolver.Resolve(thread));
            if (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
                runtime.MarkToolSnapshotDirty();
            await owner.PersistThreadIfMaterializedAsync(thread, ct);
            owner.ThreadUpdatedForBroadcast?.Invoke(thread);
        }

        private async Task<ThreadWorktreeInfo> ResolveUnboundAsync(SessionThread thread, string path, CancellationToken ct)
        {
            var fullPath = Path.GetFullPath(path, ResolveEffectiveWorkspacePath(thread));
            var status = (await ListAsync(ct)).FirstOrDefault(entry =>
                    string.Equals(Path.GetFullPath(entry.Path), fullPath, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"'{path}' is not a worktree of this workspace.");
            if (status.ThreadId != null)
                throw new InvalidOperationException("Another conversation is working in that worktree.");
            return status.Worktree with
            {
                Id = "worktree_" + Guid.NewGuid().ToString("N")[..8],
                SourceThreadId = thread.Id,
                Head = status.Head ?? string.Empty
            };
        }

        private async Task DiscardAsync(ThreadWorktreeInfo worktree) =>
            await BestEffortAsync(
                () => ThreadWorktreeManager.RemoveManagedWorktreeAndBranchAsync(
                    worktree,
                    owner.DataPath,
                    deleteBranch: true,
                    CancellationToken.None,
                    owner.Logger),
                worktree.SourceThreadId);

        private async Task BestEffortAsync(Func<Task> action, string threadId)
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                owner.Logger?.LogWarning(ex, "Worktree cleanup failed for thread {ThreadId}.", threadId);
            }
        }

        private async Task<SessionThread?> TryGetThreadAsync(string threadId, CancellationToken ct)
        {
            try
            {
                return await owner.GetOrLoadThreadAsync(threadId, ct);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        private static ThreadWorktreeInfo? ResolveRemovalWorktree(
            WorktreeRemoveOptions options,
            SessionThread? thread)
        {
            if (thread?.Worktree != null)
                return thread.Worktree;

            if (string.IsNullOrWhiteSpace(options.WorkspacePath)
                || string.IsNullOrWhiteSpace(options.Path)
                || string.IsNullOrWhiteSpace(options.BranchName))
            {
                return null;
            }

            var workspacePath = Path.GetFullPath(options.WorkspacePath);
            return new ThreadWorktreeInfo
            {
                Id = "worktree_remove_" + Guid.NewGuid().ToString("N")[..8],
                SourceThreadId = thread?.Id ?? string.Empty,
                WorkspacePath = workspacePath,
                SourceWorkspacePath = workspacePath,
                Path = Path.GetFullPath(options.Path),
                BranchName = options.BranchName.Trim(),
                BaseRef = "HEAD",
                BaseHead = string.Empty,
                Head = string.Empty,
                OwnerKind = options.BranchName.StartsWith("dotcraft/automation-", StringComparison.Ordinal)
                    ? "automationRun"
                    : null,
                CreatedAt = DateTimeOffset.UtcNow
            };
        }

        private static void ThrowIfWorktreeIdentityMovesStateWorkspace(
            SessionThread source,
            SessionIdentity? identity)
        {
            if (identity == null || string.IsNullOrWhiteSpace(identity.WorkspacePath))
                return;

            var requestedWorkspace = Path.GetFullPath(identity.WorkspacePath);
            var sourceWorkspace = Path.GetFullPath(source.WorkspacePath);
            if (!string.Equals(requestedWorkspace, sourceWorkspace, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "identity.workspacePath for worktree/createAndFork must match the source thread workspace; use executionWorkspaceOverride for the worktree path.");
            }
        }

        private static string ResolveEffectiveWorkspacePath(SessionThread thread)
            => ThreadWorkspaceResolver.Resolve(thread).Cwd;

        private static string ResolveLocalWorkspacePath(SessionThread thread)
            => ThreadWorkspaceResolver.ResolveOrdinaryCwd(thread);

        private static string NormalizeWorktreeHandoffMode(string? mode)
        {
            var normalized = string.IsNullOrWhiteSpace(mode)
                ? WorktreeHandoffModes.Worktree
                : mode.Trim();
            return normalized switch
            {
                WorktreeHandoffModes.Local => WorktreeHandoffModes.Local,
                WorktreeHandoffModes.Worktree => WorktreeHandoffModes.Worktree,
                _ => throw new ArgumentException("'mode' must be 'local' or 'worktree'.")
            };
        }

        private static void ThrowIfThreadCannotHandoffWorktree(SessionThread thread)
        {
            if (thread.Status != ThreadStatus.Active)
                throw new InvalidOperationException($"Thread '{thread.Id}' is not Active (current status: {thread.Status}). Cannot handoff worktree.");
            if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                throw new InvalidOperationException($"Thread '{thread.Id}' has a running Turn. Wait for it to complete or cancel it first.");
            if (thread.QueuedInputs.Count > 0)
                throw new InvalidOperationException("Send or remove the queued messages before handing off this thread.");
        }
    }
}
