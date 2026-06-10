namespace DotCraft.Protocol;

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
                options,
                owner.Logger,
                ct);

            capturedConfig.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;

            owner._threadsPendingPermanentDeletion.TryRemove(thread.Id, out _);
            owner._runtimeRegistry.SetThread(thread);
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
        {
            var executionOverride = thread.Configuration?.ExecutionWorkspaceOverride;
            if (!string.IsNullOrWhiteSpace(executionOverride))
                return executionOverride;

            var workspaceOverride = thread.Configuration?.WorkspaceOverride;
            return string.IsNullOrWhiteSpace(workspaceOverride) ? thread.WorkspacePath : workspaceOverride;
        }

        private static string ResolveLocalWorkspacePath(SessionThread thread)
        {
            var workspaceOverride = thread.Configuration?.WorkspaceOverride;
            return string.IsNullOrWhiteSpace(workspaceOverride) ? thread.WorkspacePath : workspaceOverride;
        }

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
