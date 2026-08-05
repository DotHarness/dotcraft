using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Agents;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using QueuedTurnInput = DotCraft.Sessions.QueuedTurnInput;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using ErrorPayload = DotCraft.Sessions.ErrorPayload;
using SenderContext = DotCraft.Sessions.SenderContext;
using SystemNoticePayload = DotCraft.Sessions.SystemNoticePayload;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using ThreadSummary = DotCraft.Sessions.ThreadSummary;
using ThreadWorktreeDirtyHandoffInfo = DotCraft.Sessions.ThreadWorktreeDirtyHandoffInfo;
using TokenUsageInfo = DotCraft.Sessions.TokenUsageInfo;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// A full <see cref="ISessionService"/> implementation for AppServer unit tests.
/// Thread lifecycle methods are backed by a real <see cref="ThreadStore"/> so that
/// <c>thread/start</c>, <c>thread/list</c>, etc. produce realistic wire responses.
/// <c>SubmitInputAsync</c> yields canned <see cref="SessionEvent"/> sequences queued
/// per thread via <see cref="EnqueueSubmitEvents"/>.
/// </summary>
internal sealed class TestableSessionService : ISessionService, IThreadAgentRefreshService, ISubAgentSyntheticTurnService, ISubAgentThreadLifecycleService, ISubAgentCommunicationRuntimeProvider
{
    private readonly ThreadStore _store;
    private readonly SubAgentCommunicationRuntime _subAgentCommunicationRuntime = new();
    private readonly Dictionary<string, SessionThread> _cache = new();
    private readonly Dictionary<string, Queue<SessionEvent[]>> _submitQueue = new();
    private readonly Lock _threadSubscribersLock = new();
    private readonly Dictionary<string, List<Channel<SessionEvent>>> _threadSubscribers = new(StringComparer.Ordinal);
    private readonly List<(string threadId, string turnId)> _cancelledTurns = new();
    private readonly List<string> _cancelledMaintenances = new();
    private readonly List<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> _resolvedApprovals = new();
    private readonly List<(string threadId, string turnId, string requestId, RequestUserInputResponse response)> _resolvedUserInputs = new();
    private readonly List<SessionEventType> _yieldedSubmitEventTypes = new();

    public IReadOnlyList<(string threadId, string turnId)> CancelledTurns => _cancelledTurns;
    public IReadOnlyList<string> CancelledMaintenances => _cancelledMaintenances;
    public IReadOnlyList<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> ResolvedApprovals => _resolvedApprovals;
    public IReadOnlyList<(string threadId, string turnId, string requestId, RequestUserInputResponse response)> ResolvedUserInputs => _resolvedUserInputs;
    public IReadOnlyList<SessionEventType> YieldedSubmitEventTypes => _yieldedSubmitEventTypes;
    public IReadOnlyList<AIContent> LastSubmittedContent { get; private set; } = [];
    public QueuedTurnInput? LastStartedQueuedInput { get; private set; }
    public IReadOnlyList<ChatMessage>? LastSubmittedMessages { get; private set; }
    public CancellationToken LastSubmitCancellationToken { get; private set; }
    public Func<string, IList<AIContent>, ChatMessage[]?, IEnumerable<SessionEvent>>? SubmitInputHandler { get; set; }
    public Func<SessionThread, CancellationToken, Task>? CreateThreadHandler { get; set; }
    public Func<string, CancellationToken, Task<ThreadMemoryConsolidationResult>>? ConsolidateThreadMemoryHandler { get; set; }
    public Func<SessionThread, ThreadSummaryRuntime>? RuntimeSnapshotHandler { get; set; }
    public IReadOnlyList<string> RefreshedThreadAgents => _refreshedThreadAgents;
    private readonly List<string> _refreshedThreadAgents = new();

    SubAgentCommunicationRuntime ISubAgentCommunicationRuntimeProvider.CommunicationRuntime =>
        _subAgentCommunicationRuntime;

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadUpdatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;

    /// <inheritdoc />
    public ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread) =>
        RuntimeSnapshotHandler?.Invoke(thread) ?? ThreadSummaryRuntime.FromThread(thread);

    public TestableSessionService(ThreadStore store) => _store = store;

    public async Task<SessionThread> UpdateThreadSourceControlTargetAsync(
        string threadId,
        ThreadSourceControlTarget? target,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (target == null)
        {
            ThreadSourceControlMetadata.ClearSourceControlTarget(thread.Metadata);
        }
        else
        {
            ThreadSourceControlMetadata.ApplyPerforceTarget(thread.Metadata, target.Changelist);
        }

        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadUpdatedForBroadcast?.Invoke(thread);
        return thread;
    }

    // -------------------------------------------------------------------------
    // Canned event configuration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queues a batch of events to be returned by the next <c>SubmitInputAsync</c>
    /// call for the given thread. Multiple calls enqueue multiple batches.
    /// </summary>
    public void EnqueueSubmitEvents(string threadId, params SessionEvent[] events)
    {
        if (!_submitQueue.TryGetValue(threadId, out var queue))
            _submitQueue[threadId] = queue = new Queue<SessionEvent[]>();
        queue.Enqueue(events);
    }

    public void PublishThreadEvent(SessionEvent evt)
    {
        lock (_threadSubscribersLock)
        {
            if (!_threadSubscribers.TryGetValue(evt.ThreadId, out var subscribers))
                return;
            foreach (var subscriber in subscribers.ToList())
            {
                subscriber.Writer.TryWrite(evt);
            }
        }
    }

    public async Task WaitForThreadSubscriberAsync(
        string threadId,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (_threadSubscribersLock)
            {
                if (_threadSubscribers.TryGetValue(threadId, out var subscribers) && subscribers.Count > 0)
                    return;
            }

            await Task.Delay(10, ct);
        }

        throw new TimeoutException($"Timed out waiting for a thread subscriber on '{threadId}'.");
    }

    // -------------------------------------------------------------------------
    // ISessionService — thread lifecycle (backed by ThreadStore)
    // -------------------------------------------------------------------------

    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null)
    {
        var thread = new SessionThread
        {
            Id = threadId ?? SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            HistoryMode = historyMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = config,
            DisplayName = displayName,
            Source = source ?? ThreadSource.User()
        };
        if (identity.ChannelContext != null)
        {
            thread.ChannelContext = identity.ChannelContext;
            thread.Metadata["channelContext"] = identity.ChannelContext;
        }
        if (CreateThreadHandler != null)
            await CreateThreadHandler(thread, ct);

        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadCreatedForBroadcast?.Invoke(thread);
        return thread;
    }

    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status != ThreadStatus.Active)
        {
            t.Status = ThreadStatus.Active;
            t.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(t, ct);
        }
        return t;
    }

    // M-iv: back the Interactive Tool UI widgetState side store with the real ThreadStore so
    // thread/read enrichment is exercised end-to-end in AppServer tests.
    public IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId)
        => _store.LoadItemWidgetStates(threadId);

    public void SetItemWidgetState(string threadId, string callId, string? widgetStateJson)
    {
        if (string.IsNullOrWhiteSpace(widgetStateJson))
            _store.DeleteItemWidgetState(threadId, callId);
        else
            _store.SaveItemWidgetState(threadId, callId, widgetStateJson);
    }

    /// <summary>Seeds a thread (with any pre-built turns/items) into the store + cache for tests.</summary>
    public async Task SeedThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
    }

    public async Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var active = await FindThreadsAsync(identity, includeArchived: false, crossChannelOrigins: null, ct);
        var archived = new List<string>();
        foreach (var summary in active.Where(s => s.Status is ThreadStatus.Active or ThreadStatus.Paused))
        {
            await ArchiveThreadAsync(summary.Id, ct);
            archived.Add(summary.Id);
        }

        var thread = await CreateThreadAsync(identity, config, historyMode, displayName: displayName, ct: ct);
        return new ThreadResetResult { Thread = thread, ArchivedThreadIds = archived, CreatedLazily = true };
    }

    public async Task<SessionThread> ForkThreadAsync(
        string threadId,
        ThreadForkOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new ThreadForkOptions();
        var source = string.IsNullOrWhiteSpace(options.Path)
            ? await GetOrLoadAsync(threadId, ct)
            : await _store.LoadThreadFromPathAsync(options.Path, ct)
                ?? throw new KeyNotFoundException($"Thread rollout path '{options.Path}' was not found.");
        if (!string.Equals(source.Id, threadId, StringComparison.Ordinal))
            throw new ArgumentException($"Thread rollout path belongs to '{source.Id}', not requested thread '{threadId}'.");

        var identity = options.Identity ?? new SessionIdentity
        {
            ChannelName = source.OriginChannel,
            UserId = source.UserId,
            ChannelContext = source.ChannelContext,
            WorkspacePath = source.WorkspacePath
        };
        if (string.IsNullOrWhiteSpace(identity.ChannelName) || string.IsNullOrWhiteSpace(identity.WorkspacePath))
        {
            identity = identity with
            {
                ChannelName = string.IsNullOrWhiteSpace(identity.ChannelName) ? source.OriginChannel : identity.ChannelName,
                WorkspacePath = string.IsNullOrWhiteSpace(identity.WorkspacePath) ? source.WorkspacePath : identity.WorkspacePath
            };
        }

        var now = DateTimeOffset.UtcNow;
        var forkTurns = CloneForkTurns(source, options.ForkPoint, now);
        var fork = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId ?? source.UserId,
            OriginChannel = identity.ChannelName,
            ChannelContext = identity.ChannelContext ?? source.ChannelContext,
            Status = ThreadStatus.Active,
            HistoryMode = source.HistoryMode,
            CreatedAt = now,
            LastActiveAt = now,
            Configuration = options.Config ?? CloneThreadConfiguration(source.Configuration),
            DisplayName = ResolveForkDisplayName(options.DisplayName, source, forkTurns),
            Source = ThreadSource.User(),
            ForkedFromId = source.Id,
            Ephemeral = options.Ephemeral,
            Worktree = options.Worktree,
            Metadata = CopyForkMetadata(source.Metadata),
            Turns = forkTurns,
            QueuedInputs = []
        };
        foreach (var turn in fork.Turns)
        {
            turn.ThreadId = fork.Id;
            foreach (var item in turn.Items)
                item.TurnId = turn.Id;
            turn.Input = ResolveTurnInput(turn);
        }
        AppendForkBoundaryNotice(fork.Turns, fork.Id, source.Id, now);

        if (fork.ChannelContext == null)
            fork.Metadata.Remove("channelContext");
        else
            fork.Metadata["channelContext"] = fork.ChannelContext;

        _cache[fork.Id] = fork;
        if (!fork.Ephemeral)
            await _store.SaveThreadAsync(fork, ct);
        ThreadCreatedForBroadcast?.Invoke(fork);
        return fork;
    }

    private static Dictionary<string, string> CopyForkMetadata(IReadOnlyDictionary<string, string> source)
    {
        var metadata = new Dictionary<string, string>(source, StringComparer.Ordinal);
        foreach (var key in metadata.Keys
            .Where(key =>
                key.StartsWith("subagent.", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("dotcraft.worktree", StringComparison.OrdinalIgnoreCase)
                || key.StartsWith("sourceControl.", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "dotcraft.internal", StringComparison.OrdinalIgnoreCase))
            .ToList())
        {
            metadata.Remove(key);
        }
        return metadata;
    }

    public async Task<WorktreeCreateAndForkResult> CreateWorktreeAndForkAsync(
        WorktreeCreateAndForkOptions options,
        CancellationToken ct = default)
    {
        var source = await GetOrLoadAsync(options.SourceThreadId, ct);
        if (options.Identity != null
            && !string.IsNullOrWhiteSpace(options.Identity.WorkspacePath)
            && !string.Equals(
                Path.GetFullPath(options.Identity.WorkspacePath),
                Path.GetFullPath(source.WorkspacePath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("identity.workspacePath for worktree/createAndFork must match the source thread workspace.");
        }

        var worktree = await ThreadWorktreeManager.CreateAsync(
            source,
            ResolveEffectiveWorkspacePath(source),
            options,
            logger: null,
            ct);
        var config = options.Config != null
            ? CloneThreadConfiguration(options.Config) ?? new ThreadConfiguration()
            : CloneThreadConfiguration(source.Configuration) ?? new ThreadConfiguration();
        config.ExecutionWorkspaceOverride = worktree.Path;
        var identity = options.Identity == null
            ? null
            : options.Identity with { WorkspacePath = source.WorkspacePath };
        var thread = await ForkThreadAsync(
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

    public async Task<WorktreeCreateAndStartResult> CreateWorktreeAndStartAsync(
        WorktreeCreateAndStartOptions options,
        CancellationToken ct = default)
    {
        var thread = await CreateThreadAsync(
            options.Identity,
            options.Config,
            options.HistoryMode,
            displayName: options.DisplayName,
            ct: ct,
            source: options.Source);
        var worktree = await ThreadWorktreeManager.CreateAsync(
            thread,
            ResolveEffectiveWorkspacePath(thread),
            options,
            logger: null,
            ct);
        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
        thread.Worktree = worktree;
        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        return new WorktreeCreateAndStartResult
        {
            Thread = thread,
            Worktree = worktree
        };
    }

    public async Task<WorktreeHandoffResult> HandoffThreadWorktreeAsync(
        WorktreeHandoffOptions options,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(options.ThreadId, ct);
        var mode = string.IsNullOrWhiteSpace(options.Mode) ? WorktreeHandoffModes.Worktree : options.Mode.Trim();
        if (mode == WorktreeHandoffModes.Worktree)
        {
            var worktree = await ThreadWorktreeManager.CreateAsync(
                thread,
                ResolveEffectiveWorkspacePath(thread),
                options,
                logger: null,
                ct);
            thread.Configuration ??= new ThreadConfiguration();
            thread.Configuration.ExecutionWorkspaceOverride = worktree.Path;
            thread.Worktree = worktree;
            _cache[thread.Id] = thread;
            await _store.SaveThreadAsync(thread, ct);
            ThreadUpdatedForBroadcast?.Invoke(thread);
            return new WorktreeHandoffResult
            {
                Thread = thread,
                Mode = WorktreeHandoffModes.Worktree,
                Worktree = worktree,
                DirtyHandoff = worktree.DirtyHandoff
            };
        }

        if (mode != WorktreeHandoffModes.Local)
            throw new ArgumentException("'mode' must be 'local' or 'worktree'.");

        ThreadWorktreeDirtyHandoffInfo? dirtyHandoff = null;
        if (thread.Worktree != null)
        {
            dirtyHandoff = await ThreadWorktreeManager.MoveBranchBackToLocalAndRemoveAsync(
                thread.Worktree,
                thread.Configuration?.WorkspaceOverride ?? thread.WorkspacePath,
                ct);
        }

        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.ExecutionWorkspaceOverride = null;
        thread.Worktree = null;
        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadUpdatedForBroadcast?.Invoke(thread);
        return new WorktreeHandoffResult
        {
            Thread = thread,
            Mode = WorktreeHandoffModes.Local,
            DirtyHandoff = dirtyHandoff
        };
    }

    public async Task<IReadOnlyList<ThreadWorktreeStatus>> ListWorktreesAsync(
        SessionIdentity? identity = null,
        CancellationToken ct = default)
    {
        var summaries = await _store.LoadIndexAsync(ct);
        var statuses = new List<ThreadWorktreeStatus>();
        foreach (var summary in summaries)
        {
            if (summary.Worktree == null)
                continue;
            if (identity != null
                && !string.Equals(summary.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            statuses.Add(await ThreadWorktreeManager.GetStatusAsync(summary.Id, summary.Worktree, ct));
        }

        return statuses;
    }

    public async Task<ThreadWorktreeStatus> GetWorktreeStatusAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Worktree == null)
            throw new InvalidOperationException($"Thread '{thread.Id}' is not bound to a DotCraft worktree.");
        return await ThreadWorktreeManager.GetStatusAsync(thread.Id, thread.Worktree, ct);
    }

    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Paused) return;
        t.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "archive");
        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Archived) continue;
            await ArchiveCoreAsync(thread, ct);
        }
    }

    public async Task ArchiveSubAgentTreeForCloseAsync(string childThreadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(childThreadId, ct);
        if (!IsSubAgentThread(root))
            throw new InvalidOperationException($"Thread '{childThreadId}' is not a SubAgent child thread.");

        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Archived) continue;
            await ArchiveCoreAsync(thread, ct);
        }
    }

    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "unarchive");
        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: false, ct))
        {
            var thread = await GetOrLoadAsync(id, ct);
            if (thread.Status == ThreadStatus.Active) continue;
            var previousStatus = thread.Status;
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(thread, ct);
            ThreadStatusChangedForBroadcast?.Invoke(thread.Id, previousStatus, thread.Status);
        }
    }

    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        var previous = t.DisplayName;
        t.DisplayName = displayName;
        await _store.SaveThreadAsync(t, ct);
        if (previous != displayName)
            ThreadRenamedForBroadcast?.Invoke(t);
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false,
        ThreadDiscoveryScope scope = ThreadDiscoveryScope.Identity)
    {
        var index = await _store.LoadIndexAsync(ct);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        return index
            .Where(s =>
            {
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;
                if (!includeSubAgents && (string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (scope == ThreadDiscoveryScope.Workspace)
                    return true;
                if (includeSubAgents
                    && (string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
                    && (identity.UserId == null || s.UserId == identity.UserId))
                    return true;

                var identityMatch =
                    (identity.UserId == null || s.UserId == identity.UserId)
                    && (identity.ChannelContext == null
                        ? s.ChannelContext == null
                        : s.ChannelContext == identity.ChannelContext);

                if (identityMatch)
                    return true;

                if (!hasCross)
                    return false;

                foreach (var o in crossChannelOrigins!)
                {
                    if (string.Equals(o, s.OriginChannel, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            })
            .Select(summary =>
            {
                if (!_cache.TryGetValue(summary.Id, out var cachedThread))
                    return summary;

                var hydrated = ThreadSummary.FromThread(cachedThread);
                hydrated.Runtime = GetThreadRuntimeSnapshot(cachedThread);
                return hydrated;
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public async Task<int> CountWorkspaceThreadsAsync(string workspacePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
            return 0;

        var index = await _store.LoadIndexAsync(ct);
        return index.Count(s =>
            string.Equals(s.WorkspacePath, workspacePath, StringComparison.OrdinalIgnoreCase)
            && !ThreadVisibility.IsInternal(s)
            && !(string.Equals(s.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
    {
        await _store.UpsertThreadSpawnEdgeAsync(edge, ct);
        var child = await GetOrLoadAsync(edge.ChildThreadId, ct);
        _subAgentCommunicationRuntime.PublishGraph(ResolveRootThreadId(child));
    }

    public async Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
    {
        await _store.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);
        var child = await GetOrLoadAsync(childThreadId, ct);
        _subAgentCommunicationRuntime.PublishGraph(ResolveRootThreadId(child));
    }

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default) =>
        _store.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);

    public async Task AddSubAgentMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct = default)
    {
        await _store.AddSubAgentMailboxEntryAsync(entry, ct);
        _subAgentCommunicationRuntime.PublishMailbox(entry.RootThreadId, entry.TargetAgentPath);
    }

    public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingSubAgentMailboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct = default) =>
        _store.ListPendingSubAgentMailboxAsync(rootThreadId, targetAgentPath, ct);

    public Task MarkSubAgentMailboxDeliveredAsync(
        string rootThreadId,
        IReadOnlyList<string> entryIds,
        DateTimeOffset deliveredAt,
        CancellationToken ct = default) =>
        _store.MarkSubAgentMailboxDeliveredAsync(rootThreadId, entryIds, deliveredAt, ct);

    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadAsync(threadId, ct);

    public async Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var snapshot = await _store.ReadThreadSnapshotAsync(threadId, ct);
        var loaded = await GetOrLoadAsync(threadId, ct);
        return snapshot with { PersistedRuntime = GetThreadRuntimeSnapshot(loaded) };
    }

    public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        _store.ListThreadTurnsAsync(threadId, cursor, limit, direction, ct);

    public Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default) =>
        _store.ListThreadItemsAsync(threadId, turnId, cursor, limit, direction, ct);

    public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
        GetThreadAsync(threadId, ct);

    public async Task<ThreadGoal?> GetThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        return await _store.GetThreadGoalAsync(threadId, ct);
    }

    public async Task<ThreadGoal> SetThreadGoalAsync(
        string threadId,
        ThreadGoalUpdate update,
        GoalSetMode mode = GoalSetMode.UpsertOrUpdate,
        CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        var existing = await _store.GetThreadGoalAsync(threadId, ct);
        if (mode == GoalSetMode.CreateOnly && existing is { Status: not ThreadGoalStatus.Complete })
            throw new InvalidOperationException($"Thread '{threadId}' already has a goal.");
        if (mode == GoalSetMode.UpdateOnly && existing == null)
            throw new InvalidOperationException($"Thread '{threadId}' has no goal.");
        if (existing == null && string.IsNullOrWhiteSpace(update.Objective))
            throw new InvalidOperationException($"Thread '{threadId}' has no goal.");

        var objective = update.Objective?.Trim();
        var now = DateTimeOffset.UtcNow;
        var replace = existing != null
            && !string.IsNullOrWhiteSpace(objective)
            && (mode == GoalSetMode.ReplaceExisting
                || (mode == GoalSetMode.CreateOnly && existing.Status == ThreadGoalStatus.Complete));
        var goal = replace || existing == null
            ? new ThreadGoal
            {
                ThreadId = threadId,
                GoalId = SessionIdGenerator.NewGoalId(),
                Objective = objective ?? throw new InvalidOperationException("Goal objective is required."),
                Status = ThreadGoalStatus.Active,
                TokenBudget = update.HasTokenBudget ? update.TokenBudget : null,
                TokensUsed = new TokenUsageInfo(),
                CreatedAt = now,
                UpdatedAt = now
            }
            : existing with
            {
                Objective = objective ?? existing.Objective,
                Status = update.Status ?? existing.Status,
                TokenBudget = update.HasTokenBudget ? update.TokenBudget : existing.TokenBudget,
                UpdatedAt = now
            };
        await _store.UpsertThreadGoalAsync(goal, ct);
        return goal;
    }

    public async Task<ThreadGoalClearResult> ClearThreadGoalAsync(string threadId, CancellationToken ct = default)
    {
        _ = await GetOrLoadAsync(threadId, ct);
        return new ThreadGoalClearResult(await _store.DeleteThreadGoalAsync(threadId, ct));
    }

    public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default)
    {
        _refreshedThreadAgents.Add(threadId);
        return Task.CompletedTask;
    }

    public void InvalidateThreadAgents()
    {
    }

    public async Task UpdateThreadConfigurationAsync(
        string threadId, ThreadConfiguration config, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        t.Configuration = config;
        _cache[t.Id] = t;
        await _store.SaveThreadAsync(t, ct);
        ThreadUpdatedForBroadcast?.Invoke(t);
    }

    public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        return DeleteThreadPermanentlyCoreAsync(threadId, ct);
    }

    private async Task DeleteThreadPermanentlyCoreAsync(string threadId, CancellationToken ct)
    {
        var root = await GetOrLoadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "delete");
        foreach (var id in (await CollectSubAgentSubtreeIdsAsync(root.Id, includeClosed: true, ct)).Reverse())
        {
            _cache.Remove(id);
            _store.DeleteThread(id);
            ThreadDeletedForBroadcast?.Invoke(id);
        }
    }

    private async Task ArchiveCoreAsync(SessionThread thread, CancellationToken ct)
    {
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(thread, ct);
        ThreadStatusChangedForBroadcast?.Invoke(thread.Id, previousStatus, thread.Status);
    }

    private static void ThrowIfDirectSubAgentLifecycleOperation(SessionThread thread, string operation)
    {
        if (!IsSubAgentThread(thread)) return;
        var parentId = thread.Source.SubAgent?.ParentThreadId?.Trim();
        if (string.IsNullOrWhiteSpace(parentId))
            parentId = thread.ChannelContext?.Trim();
        if (!string.IsNullOrWhiteSpace(parentId))
            throw new InvalidOperationException(
                $"SubAgent child thread '{thread.Id}' cannot be {operation}d directly; manage its parent thread '{parentId}' instead.");
    }

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<string>> CollectSubAgentSubtreeIdsAsync(
        string rootThreadId,
        bool includeClosed,
        CancellationToken ct)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task VisitAsync(string id)
        {
            if (!seen.Add(id)) return;
            result.Add(id);
            var children = await _store.ListSubAgentChildrenAsync(id, includeClosed, ct);
            foreach (var child in children)
                await VisitAsync(child.ChildThreadId);
        }

        await VisitAsync(rootThreadId);
        return result;
    }

    // -------------------------------------------------------------------------
    // ISessionService — turn operations (backed by canned events)
    // -------------------------------------------------------------------------

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        LastSubmittedContent = content.ToList();
        LastSubmittedMessages = messages?.ToList();
        LastSubmitCancellationToken = ct;
        if (SubmitInputHandler != null)
            return YieldEvents([.. SubmitInputHandler(threadId, content, messages)], ct);
        if (_submitQueue.TryGetValue(threadId, out var queue) && queue.TryDequeue(out var events))
            return YieldEvents(events, ct);

        return EmptyEvents();
    }

    public async Task<SessionTurn> StartSubAgentSyntheticTurnAsync(
        string threadId,
        IList<AIContent> content,
        string runtimeType,
        string? profileName,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var text = string.Concat(content.OfType<TextContent>().Select(c => c.Text));
        var triggerInfo = TurnTriggerScope.Current;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = thread.OriginChannel
        };
        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = text,
                TriggerKind = triggerInfo?.Kind,
                TriggerLabel = triggerInfo?.Label,
                TriggerRefId = triggerInfo?.RefId
            }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.Metadata["subagent.syntheticRuntime"] = runtimeType;
        if (!string.IsNullOrWhiteSpace(profileName))
            thread.Metadata["subagent.profileName"] = profileName;
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var turn = thread.Turns.Single(t => t.Id == turnId);
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
            TurnId = turn.Id,
            Type = isError ? ItemType.Error : ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = isError
                ? new ErrorPayload { Message = text, Code = "subagent_error", Fatal = true }
                : new AgentMessagePayload { Text = text }
        };
        turn.Items.Add(item);
        turn.Status = isError ? TurnStatus.Failed : TurnStatus.Completed;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        turn.Error = isError ? text : null;
        if (tokensUsed != null)
        {
            turn.TokenUsage = new TokenUsageInfo
            {
                InputTokens = tokensUsed.InputTokens,
                OutputTokens = tokensUsed.OutputTokens,
                TotalTokens = tokensUsed.InputTokens + tokensUsed.OutputTokens
            };
        }
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var turn = thread.Turns.Single(t => t.Id == turnId);
        turn.Status = TurnStatus.Cancelled;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return turn;
    }

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        var channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        lock (_threadSubscribersLock)
        {
            if (!_threadSubscribers.TryGetValue(threadId, out var subscribers))
                _threadSubscribers[threadId] = subscribers = [];
            subscribers.Add(channel);
        }

        return ReadThreadSubscriptionAsync(threadId, channel, ct);
    }

    private async IAsyncEnumerable<SessionEvent> ReadThreadSubscriptionAsync(
        string threadId,
        Channel<SessionEvent> channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_threadSubscribersLock)
            {
                if (_threadSubscribers.TryGetValue(threadId, out var subscribers))
                {
                    subscribers.Remove(channel);
                    if (subscribers.Count == 0)
                        _threadSubscribers.Remove(threadId);
                }
            }

            channel.Writer.TryComplete();
        }
    }

    public Task ResolveApprovalAsync(
        string threadId, string turnId, string requestId,
        SessionApprovalDecision decision, CancellationToken ct = default)
    {
        _resolvedApprovals.Add((threadId, turnId, requestId, decision));
        return Task.CompletedTask;
    }

    public Task ResolveUserInputRequestAsync(
        string threadId,
        string turnId,
        string requestId,
        RequestUserInputResponse response,
        CancellationToken ct = default)
    {
        _resolvedUserInputs.Add((threadId, turnId, requestId, response));
        return Task.CompletedTask;
    }

    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
    {
        _cancelledTurns.Add((threadId, turnId));
        return Task.CompletedTask;
    }

    public Task CancelThreadMaintenanceAsync(string threadId, CancellationToken ct = default)
    {
        _cancelledMaintenances.Add(threadId);
        return Task.CompletedTask;
    }

    public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<ThreadMemoryConsolidationResult> ConsolidateThreadMemoryAsync(
        string threadId,
        CancellationToken ct = default) =>
        ConsolidateThreadMemoryHandler?.Invoke(threadId, ct)
        ?? Task.FromResult(new ThreadMemoryConsolidationResult
        {
            Outcome = "skipped",
            Message = "not_configured"
        });

    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
    {
        if (numTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(numTurns), "numTurns must be >= 1.");

        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be rolled back.");
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Cancel it before rollback.");
        if (thread.Turns.Count < numTurns)
            throw new InvalidOperationException($"Thread '{threadId}' has only {thread.Turns.Count} turns; cannot roll back {numTurns}.");

        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.RollbackThreadAsync(thread, numTurns, ct);
        return thread;
    }

    public async Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var parts = inputSnapshot?.NativeInputParts?.ToList() ?? content.Select(c => c.ToWireInputPart()).ToList();
        var triggerInfo = TurnTriggerScope.Current;
        var queued = new QueuedTurnInput
        {
            Id = SessionIdGenerator.NewQueuedInputId(),
            ThreadId = threadId,
            NativeInputParts = parts,
            MaterializedInputParts = inputSnapshot?.MaterializedInputParts?.ToList() ?? parts,
            DisplayText = inputSnapshot?.DisplayText ?? SessionWireMapper.BuildDisplayText(parts),
            Sender = sender,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "queued",
            TriggerKind = triggerInfo?.Kind,
            TriggerLabel = triggerInfo?.Label,
            TriggerRefId = triggerInfo?.RefId,
            DeliveryBindingId = inputSnapshot?.DeliveryBindingId,
            SentAsGoal = inputSnapshot?.SentAsGoal
        };
        thread.QueuedInputs.Add(queued);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return queued;
    }

    public async Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            return;

        var queued = thread.QueuedInputs.FirstOrDefault(q => string.Equals(q.Status, "queued", StringComparison.Ordinal));
        if (queued == null)
            return;

        thread.QueuedInputs.Remove(queued);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);

        LastStartedQueuedInput = queued;
        LastSubmittedContent = await SessionInputPartResolver.ResolvePersistedAsync(
            queued.MaterializedInputParts,
            ct);
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.QueuedInputs.RemoveAll(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return thread.QueuedInputs.ToList();
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> ReorderQueuedTurnInputsAsync(
        string threadId,
        IReadOnlyList<string> orderedQueuedInputIds,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (orderedQueuedInputIds.Count != thread.QueuedInputs.Count)
            throw new ArgumentException("orderedQueuedInputIds must contain every current queued input ID exactly once.");
        var byId = thread.QueuedInputs.ToDictionary(q => q.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reordered = new List<QueuedTurnInput>(thread.QueuedInputs.Count);
        foreach (var queuedInputId in orderedQueuedInputIds)
        {
            if (!seen.Add(queuedInputId) || !byId.TryGetValue(queuedInputId, out var queuedInput))
                throw new ArgumentException("orderedQueuedInputIds must contain every current queued input ID exactly once.");
            reordered.Add(queuedInput);
        }

        thread.QueuedInputs = reordered;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(thread, ct);
        return thread.QueuedInputs.ToList();
    }

    public async Task<IReadOnlyList<QueuedTurnInput>> UpdateQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        string expectedTurnId,
        string status,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        var index = thread.QueuedInputs.FindIndex(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
        if (index < 0)
            throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");
        var queued = thread.QueuedInputs[index];
        if (string.Equals(queued.Status, status, StringComparison.Ordinal))
            return thread.QueuedInputs.ToList();
        if (string.Equals(status, "guidancePending", StringComparison.Ordinal))
        {
            var turn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                ?? throw new InvalidOperationException("No active turn.");
            if (!string.Equals(turn.Id, expectedTurnId, StringComparison.Ordinal))
                throw new InvalidOperationException("Active turn mismatch.");
            thread.QueuedInputs[index] = queued with { Status = "guidancePending", ReadyAfterTurnId = turn.Id };
        }
        else if (string.Equals(status, "queued", StringComparison.Ordinal))
        {
            if (!string.Equals(queued.Status, "guidancePending", StringComparison.Ordinal)
                || !string.Equals(queued.ReadyAfterTurnId, expectedTurnId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Pending guidance turn mismatch.");
            }
            thread.QueuedInputs[index] = queued with { Status = "queued" };
        }
        else
        {
            throw new InvalidOperationException("Invalid queued input status.");
        }

        await _store.SaveThreadAsync(thread, ct);
        if (string.Equals(status, "guidancePending", StringComparison.Ordinal))
        {
            var targetAgentPath = string.IsNullOrWhiteSpace(thread.Source.SubAgent?.AgentPath)
                ? AgentPath.Root
                : thread.Source.SubAgent.AgentPath;
            _subAgentCommunicationRuntime.PublishSteer(ResolveRootThreadId(thread), targetAgentPath);
        }
        return thread.QueuedInputs.ToList();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string ResolveRootThreadId(SessionThread thread) =>
        string.IsNullOrWhiteSpace(thread.Source.SubAgent?.RootThreadId)
            ? thread.Id
            : thread.Source.SubAgent.RootThreadId;

    private async Task<SessionThread> GetOrLoadAsync(string threadId, CancellationToken ct)
    {
        if (_cache.TryGetValue(threadId, out var cached)) return cached;
        var loaded = await _store.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        _cache[threadId] = loaded;
        return loaded;
    }

    private static ThreadConfiguration? CloneThreadConfiguration(ThreadConfiguration? source)
    {
        if (source == null)
            return null;
        var json = JsonSerializer.Serialize(source, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<ThreadConfiguration>(json, SessionJsonOptions.Default);
    }

    private static string ResolveEffectiveWorkspacePath(SessionThread thread)
    {
        var executionOverride = thread.Configuration?.ExecutionWorkspaceOverride;
        if (!string.IsNullOrWhiteSpace(executionOverride))
            return executionOverride;

        var workspaceOverride = thread.Configuration?.WorkspaceOverride;
        return string.IsNullOrWhiteSpace(workspaceOverride) ? thread.WorkspacePath : workspaceOverride;
    }

    private static List<SessionTurn> CloneForkTurns(SessionThread source, ThreadForkPoint? forkPoint, DateTimeOffset now)
    {
        var json = JsonSerializer.Serialize(source.Turns, SessionJsonOptions.Default);
        var turns = JsonSerializer.Deserialize<List<SessionTurn>>(json, SessionJsonOptions.Default) ?? [];
        if (forkPoint == null)
            return turns;

        if (string.IsNullOrWhiteSpace(forkPoint.TurnId))
            throw new ArgumentException("forkPoint.turnId is required when forkPoint is provided.");
        var turnIndex = turns.FindIndex(turn => string.Equals(turn.Id, forkPoint.TurnId, StringComparison.Ordinal));
        if (turnIndex < 0)
            throw new ArgumentException($"forkPoint.turnId '{forkPoint.TurnId}' was not found.");
        var includePoint = string.IsNullOrWhiteSpace(forkPoint.Position)
            || string.Equals(forkPoint.Position, ThreadForkPositions.After, StringComparison.OrdinalIgnoreCase);
        if (!includePoint && !string.Equals(forkPoint.Position, ThreadForkPositions.Before, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("forkPoint.position must be 'before' or 'after'.");

        if (string.IsNullOrWhiteSpace(forkPoint.ItemId))
            return includePoint ? turns.Take(turnIndex + 1).ToList() : turns.Take(turnIndex).ToList();

        var target = turns[turnIndex];
        var itemIndex = target.Items.FindIndex(item => string.Equals(item.Id, forkPoint.ItemId, StringComparison.Ordinal));
        if (itemIndex < 0)
            throw new ArgumentException($"forkPoint.itemId '{forkPoint.ItemId}' was not found in turn '{forkPoint.TurnId}'.");
        var itemCount = includePoint ? itemIndex + 1 : itemIndex;
        var selected = turns.Take(turnIndex).ToList();
        if (itemCount <= 0)
            return selected;

        var originalCount = target.Items.Count;
        target.Items = target.Items.Take(itemCount).ToList();
        target.Input = ResolveTurnInput(target);
        if (itemCount < originalCount || target.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
        {
            target.Status = TurnStatus.Cancelled;
            target.CompletedAt ??= now;
            target.Error ??= "Forked from a partial source turn.";
        }

        selected.Add(target);
        return selected;
    }

    private static string? ResolveForkDisplayName(
        string? explicitDisplayName,
        SessionThread source,
        IReadOnlyList<SessionTurn> forkedTurns)
    {
        if (!string.IsNullOrWhiteSpace(explicitDisplayName))
            return explicitDisplayName;

        if (!string.IsNullOrWhiteSpace(source.DisplayName))
            return source.DisplayName;

        return FirstUserMessageDisplayName(forkedTurns);
    }

    private static string? FirstUserMessageDisplayName(IReadOnlyList<SessionTurn> turns)
    {
        foreach (var turn in turns)
        {
            var text = (turn.Input?.Payload as UserMessagePayload)?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                text = turn.Items
                    .Where(item => item.Type == ItemType.UserMessage)
                    .Select(item => (item.Payload as UserMessagePayload)?.Text)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var trimmed = text.Trim();
            return trimmed.Length > 50 ? trimmed[..50] + "..." : trimmed;
        }

        return null;
    }

    private static void AppendForkBoundaryNotice(
        List<SessionTurn> turns,
        string forkedThreadId,
        string sourceThreadId,
        DateTimeOffset now)
    {
        if (turns.Count == 0)
        {
            var turn = new SessionTurn
            {
                Id = SessionIdGenerator.NewTurnId(1),
                ThreadId = forkedThreadId,
                Status = TurnStatus.Completed,
                StartedAt = now,
                CompletedAt = now,
                Items = []
            };
            turn.Items.Add(CreateForkBoundaryNoticeItem(turn, 1, sourceThreadId, now));
            turns.Add(turn);
            return;
        }

        var targetTurn = turns[^1];
        targetTurn.Items.Add(CreateForkBoundaryNoticeItem(
            targetTurn,
            targetTurn.Items.Count + 1,
            sourceThreadId,
            now));
    }

    private static SessionItem CreateForkBoundaryNoticeItem(
        SessionTurn turn,
        int seq,
        string sourceThreadId,
        DateTimeOffset now)
    {
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = turn.Id,
            Type = ItemType.SystemNotice,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new SystemNoticePayload
            {
                Kind = "forked",
                SourceThreadId = sourceThreadId
            }
        };
    }

    private static SessionItem? ResolveTurnInput(SessionTurn turn)
    {
        var inputId = turn.Input?.Id;
        return !string.IsNullOrWhiteSpace(inputId)
            ? turn.Items.FirstOrDefault(item => string.Equals(item.Id, inputId, StringComparison.Ordinal))
                ?? turn.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage)
            : turn.Items.FirstOrDefault(item => item.Type == ItemType.UserMessage);
    }

    private async IAsyncEnumerable<SessionEvent> YieldEvents(
        SessionEvent[] events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            ct.ThrowIfCancellationRequested();
            _yieldedSubmitEventTypes.Add(e.EventType);
            yield return e;
        }
    }

    private static async IAsyncEnumerable<SessionEvent> EmptyEvents()
    {
        yield break;
    }
}
