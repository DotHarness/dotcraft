namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class WorktreeCoordinator(SessionService owner)
    {
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
                owner.Logger,
                ct);

            var config = options.Config != null
                ? CloneThreadConfiguration(options.Config)
                : source.Configuration != null
                    ? CloneThreadConfiguration(source.Configuration)
                    : new ThreadConfiguration();
            config.ExecutionWorkspaceOverride = worktree.Path;

            var identity = options.Identity == null
                ? null
                : options.Identity with { WorkspacePath = source.WorkspacePath };

            var thread = await owner.ForkThreadAsync(
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
                owner.Logger,
                ct);

            capturedConfig.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;

            owner._runtimeRegistry.SetThread(thread);
            owner._runtimeRegistry.ClearPendingPermanentDeletion(thread.Id);
            var broker = owner.GetOrCreateBroker(thread.Id);

            using (await owner.AcquireThreadAgentLockAsync(thread.Id, ct))
                owner.SetThreadAgent(thread.Id, await owner.BuildAgentForThreadAsync(thread, ct));

            await owner.PersistThreadWithMaterializationAsync(thread, ct);
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
                    var worktree = await ThreadWorktreeManager.CreateAsync(
                        thread,
                        sourceExecutionWorkspace,
                        owner.DataPath,
                        options,
                        owner.Logger,
                        ct);

                    thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                    thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
                    thread.Worktree = worktree;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;

                    await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                    owner.ThreadUpdatedForBroadcast?.Invoke(thread);
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
                    ct,
                    owner.Logger);

                thread.Configuration ??= owner.CaptureThreadConfigurationForNewThread(null);
                thread.Configuration.ExecutionWorkspaceOverride = null;
                thread.Worktree = null;
                thread.LastActiveAt = DateTimeOffset.UtcNow;

                await owner.RebuildAgentAndPersistThreadAsync(thread, ct);
                owner.ThreadUpdatedForBroadcast?.Invoke(thread);
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

        public async Task<IReadOnlyList<ThreadWorktreeStatus>> ListAsync(
            SessionIdentity? identity,
            CancellationToken ct)
        {
            var summaries = await owner.Persistence.LoadIndexAsync(ct);
            var byThreadId = new Dictionary<string, ThreadWorktreeInfo>(StringComparer.Ordinal);
            foreach (var summary in summaries)
            {
                if (summary.Worktree == null)
                    continue;
                if (identity != null
                    && !string.Equals(summary.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byThreadId[summary.Id] = summary.Worktree;
            }

            foreach (var thread in owner._runtimeRegistry.Values.Select(runtime => runtime.Thread))
            {
                if (thread.Ephemeral || thread.Worktree == null)
                    continue;
                if (identity != null
                    && !string.Equals(thread.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                byThreadId[thread.Id] = thread.Worktree;
            }

            var statuses = new List<ThreadWorktreeStatus>(byThreadId.Count);
            foreach (var (threadId, worktree) in byThreadId)
            {
                statuses.Add(await ThreadWorktreeManager.GetStatusAsync(threadId, worktree, ct, owner.Logger));
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
                OwnerKind = options.BranchName.StartsWith("dotcraft/task-", StringComparison.Ordinal)
                    ? "automationTask"
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
        }
    }
}
