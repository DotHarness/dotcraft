namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private WorktreeCreationCoordinator? _worktreeCreationCoordinator;

    private WorktreeCreationCoordinator WorktreeCreation =>
        _worktreeCreationCoordinator ??= new WorktreeCreationCoordinator(this);

    private sealed class WorktreeCreationCoordinator(SessionService owner)
    {
        public async Task<WorktreeCreateAndForkResult> CreateAndForkAsync(
            WorktreeCreateAndForkOptions options,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(options);
            var sourceThreadId = NormalizeRequiredThreadId(options.SourceThreadId);
            var source = await owner.GetOrLoadThreadAsync(sourceThreadId, ct);
            ThrowIfWorktreeIdentityMovesStateWorkspace(source, options.Identity);

            var sourceExecutionWorkspace = ThreadWorkspaceResolver.Resolve(source).Cwd;
            using var operation = owner.WorktreeEnvironments.Begin(options.OperationId, ct);
            var (worktree, setup) = await CreateWorktreeAsync(
                source,
                sourceExecutionWorkspace,
                source.Worktree is { } sourceWorktree ? WorktreeEnvironmentCoordinator.SourceWorkspaceOf(sourceWorktree) : sourceExecutionWorkspace,
                new WorktreeCreateRequest(
                    options.DisplayName,
                    options.BranchName,
                    options.BaseRef,
                    options.Path,
                    options.CopyDirtyChanges ? DirtyChangeHandling.Copy : DirtyChangeHandling.None,
                    owner.WorktreeBranchPrefix,
                    options.Progress,
                    ReportCopyStep: true),
                options.EnvironmentPath,
                operation,
                options.SetupOutput);
            if (source.Worktree is { } boundWorktree)
                worktree = worktree with { SourceWorkspacePath = boundWorktree.SourceWorkspacePath };

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
                operation.Token.ThrowIfCancellationRequested();
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
                await owner.Worktrees.DiscardAsync(worktree);
                if (operation.IsCancelled)
                    throw CreationCancelled();
                throw;
            }

            return new WorktreeCreateAndForkResult
            {
                Thread = thread,
                Worktree = worktree,
                Setup = setup
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

            var sourceExecutionWorkspace = ThreadWorkspaceResolver.ResolveOrdinaryCwd(thread);
            using var operation = owner.WorktreeEnvironments.Begin(options.OperationId, ct);
            var (worktree, setup) = await CreateWorktreeAsync(
                thread,
                sourceExecutionWorkspace,
                sourceExecutionWorkspace,
                new WorktreeCreateRequest(
                    options.DisplayName,
                    options.BranchName,
                    options.BaseRef,
                    options.Path,
                    options.CopyDirtyChanges ? DirtyChangeHandling.Copy : DirtyChangeHandling.None,
                    owner.WorktreeBranchPrefix,
                    options.Progress,
                    ReportCopyStep: true),
                options.EnvironmentPath,
                operation,
                options.SetupOutput);

            capturedConfig.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;

            owner._runtimeRegistry.SetThread(thread);
            owner._runtimeRegistry.ClearPendingPermanentDeletion(thread.Id);
            var broker = owner.GetOrCreateBroker(thread.Id);

            try
            {
                using (await owner.AcquireThreadAgentLockAsync(thread.Id, operation.Token))
                    owner.SetThreadAgent(thread.Id, await owner.BuildAgentForThreadAsync(thread, operation.Token));

                operation.Token.ThrowIfCancellationRequested();
                await owner.PersistThreadWithMaterializationAsync(thread, ct);
            }
            catch
            {
                if (owner._runtimeRegistry.TryRemove(thread.Id, out var removed))
                    await removed.DisposeAsync();
                await owner.Worktrees.DiscardAsync(worktree);
                if (operation.IsCancelled)
                    throw CreationCancelled();
                throw;
            }

            await owner.SaveContextUsageSnapshotAsync(thread.Id, 0, ct);

            broker.PublishThreadEvent(SessionEventType.ThreadCreated, thread);
            owner.ThreadCreatedForBroadcast?.Invoke(thread);

            return new WorktreeCreateAndStartResult
            {
                Thread = thread,
                Worktree = worktree,
                Setup = setup
            };
        }

        private async Task<(ThreadWorktreeInfo Worktree, WorktreeSetupResult? Setup)> CreateWorktreeAsync(
            SessionThread thread,
            string sourceExecutionWorkspace,
            string environmentWorkspace,
            WorktreeCreateRequest request,
            string? environmentPath,
            WorktreeEnvironmentCoordinator.Operation operation,
            Action<string>? setupOutput)
        {
            var environment = string.IsNullOrWhiteSpace(environmentPath)
                ? null
                : WorktreeEnvironment.Require(environmentPath, environmentWorkspace);

            ThreadWorktreeInfo worktree;
            try
            {
                worktree = await ThreadWorktreeManager.CreateAsync(
                    thread,
                    sourceExecutionWorkspace,
                    owner.DataPath,
                    request,
                    owner.Logger,
                    operation.Token);
            }
            catch when (operation.IsCancelled)
            {
                throw CreationCancelled();
            }

            worktree = worktree with { EnvironmentPath = environment?.FilePath };
            try
            {
                if (environment != null)
                    await WorktreeGitOperations.WriteEnvironmentPathAsync(worktree.Path, environment.FilePath, operation.Token, owner.Logger);
                var setup = await owner.WorktreeEnvironments.SetUpAsync(
                    environment,
                    worktree,
                    sourceExecutionWorkspace,
                    request.Progress,
                    setupOutput,
                    operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                return (worktree, setup);
            }
            catch
            {
                await owner.Worktrees.DiscardAsync(worktree);
                if (operation.IsCancelled)
                    throw CreationCancelled();
                throw;
            }
        }

        private static WorktreeEnvironmentException CreationCancelled() =>
            new(WorktreeEnvironmentErrorCodes.CreationCancelled, "Worktree creation was cancelled.");

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
    }
}
