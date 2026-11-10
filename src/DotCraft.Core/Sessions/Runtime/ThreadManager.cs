using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Mcp;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Context.WorldState;
using Microsoft.Extensions.AI;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Sessions;

/// <summary>Owns loaded Thread runtimes, deletion tombstones, and runtime generations.</summary>
internal sealed class ThreadManager : IAsyncDisposable
{
    private readonly DotCraft.Memory.PlanStore? _planStore;

    public ThreadManager(DotCraft.Memory.PlanStore? planStore = null)
    {
        _planStore = planStore;
        if (planStore is not null)
            planStore.Saved += OnPlanSaved;
    }

    private void OnPlanSaved(string threadId, DotCraft.Memory.StructuredPlan plan)
    {
        if (TryGetRuntime(threadId, out var runtime))
            runtime.Broker.UpdatePlan(plan, runtime.Generation);
    }

    private Task? _disposeTask;
    private bool _disposed;

    public ValueTask DisposeAsync()
    {
        lock (_registrationLock)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        ThreadRuntime[] runtimes;
        lock (_registrationLock)
        {
            _disposed = true;
            runtimes = _runtimes.Values.ToArray();
            _runtimes.Clear();
            foreach (var runtime in runtimes)
                runtime.Commands.Close();
        }
        if (_planStore is not null)
            _planStore.Saved -= OnPlanSaved;
        await Task.WhenAll(runtimes.Select(async runtime => await runtime.DisposeAsync().ConfigureAwait(false)))
            .ConfigureAwait(false);
        foreach (var broker in _brokers.Values)
            broker.Close();
        _brokers.Clear();
    }
    private readonly ConcurrentDictionary<string, ThreadRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly Lock _registrationLock = new();
    private readonly ConcurrentDictionary<string, ThreadEventBroker> _brokers = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pendingPermanentDeletion = new(StringComparer.Ordinal);

    public int Count => _runtimes.Count;

    public IReadOnlyCollection<ThreadRuntime> Values => _runtimes.Values.ToArray();

    public bool TryGetRuntime(string threadId, out ThreadRuntime runtime) =>
        _runtimes.TryGetValue(threadId, out runtime!);

    public bool IsCurrent(string threadId, ThreadRuntime runtime) =>
        _runtimes.TryGetValue(threadId, out var current) && ReferenceEquals(current, runtime);

    public bool TryGetThread(string threadId, out SessionThread thread)
    {
        if (_runtimes.TryGetValue(threadId, out var runtime))
        {
            thread = runtime.Thread;
            return true;
        }

        thread = null!;
        return false;
    }

    public ThreadEventBroker GetBroker(string threadId) =>
        _brokers.GetOrAdd(threadId, static id => new ThreadEventBroker(id));

    public ThreadRuntime SetThread(SessionThread thread)
    {
        lock (_registrationLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_runtimes.TryGetValue(thread.Id, out var runtime))
            {
                runtime = new ThreadRuntime(thread, GetBroker(thread.Id));
                _runtimes[thread.Id] = runtime;
            }
            else runtime.Thread = thread;
            runtime.Broker.Initialize(thread, runtime.Generation);
            return runtime;
        }
    }

    public bool TryRemove(string threadId, out ThreadRuntime runtime)
    {
        lock (_registrationLock)
            return _runtimes.TryRemove(threadId, out runtime!);
    }

    public bool TryRemove(ThreadRuntime runtime)
    {
        lock (_registrationLock)
            return _runtimes.TryRemove(new KeyValuePair<string, ThreadRuntime>(runtime.Thread.Id, runtime));
    }

    public void ForgetBroker(string threadId)
    {
        if (_brokers.TryRemove(threadId, out var broker)) broker.Close();
    }

    public void MarkPendingPermanentDeletion(string threadId) =>
        _pendingPermanentDeletion[threadId] = 0;

    public void ClearPendingPermanentDeletion(string threadId)
    {
        _pendingPermanentDeletion.TryRemove(threadId, out _);
    }

    public bool IsPendingPermanentDeletion(string threadId) =>
        _pendingPermanentDeletion.ContainsKey(threadId);
}

internal sealed class ThreadRuntime(SessionThread thread, ThreadEventBroker broker) : IAsyncDisposable
{
    private static long _nextGeneration;
    private readonly object _bindingMcpLock = new();
    private readonly Dictionary<string, IReadOnlyList<McpServerConfig>> _bindingMcpServers =
        new(StringComparer.Ordinal);
    private ThreadMaintenanceState? _maintenance;
    private int _goalContinuationStarting;
    private readonly ConcurrentDictionary<string, byte> _goalBudgetLimitReported = new(StringComparer.Ordinal);
    private long _toolSnapshotRevision;
    private int _toolSnapshotDirty = 1;
    private long _lastAccessTicks = DateTimeOffset.UtcNow.UtcTicks;

    public SessionThread Thread { get; set; } = thread;

    /// <summary>
    /// Distinguishes a reloaded runtime from an obsolete background task that still
    /// carries the same persisted Thread id.
    /// </summary>
    public long Generation { get; } = Interlocked.Increment(ref _nextGeneration);

    /// <summary>The single owner of externally initiated Thread state transitions.</summary>
    public ThreadCommandDispatcher Commands { get; } = new(thread.Id);

    public ThreadEventBroker Broker { get; } = broker;

    public SemaphoreSlim QueueLock { get; set; } = new(1, 1);

    public SemaphoreSlim AgentLock { get; set; } = new(1, 1);

    public ChatClientAgent? Agent { get; set; }

    public IReadOnlyList<ChatMessage>? EphemeralHistory { get; set; }

    public ResidentModelHistory? ResidentModelHistory { get; set; }

    public McpClientManager? McpManager { get; set; }

    public EventHandler<McpServerStatusChangedEventArgs>? McpStatusHandler { get; set; }

    public async ValueTask ReleaseMcpAsync()
    {
        var manager = McpManager;
        McpManager = null;
        if (manager is null) return;
        manager.StatusChanged -= McpStatusHandler;
        McpStatusHandler = null;
        await manager.DisposeAsync().ConfigureAwait(false);
    }

    public IReadOnlyList<McpServerConfig> GetBindingMcpServers()
    {
        lock (_bindingMcpLock)
            return _bindingMcpServers.Values.SelectMany(static servers => servers).Select(static server => server.Clone()).ToArray();
    }

    public void SetBindingMcpServers(string bindingId, IReadOnlyList<McpServerConfig> servers)
    {
        lock (_bindingMcpLock)
        {
            if (servers.Count == 0)
                _bindingMcpServers.Remove(bindingId);
            else
                _bindingMcpServers[bindingId] = servers.Select(static server => server.Clone()).ToArray();
        }
        MarkToolSnapshotDirty();
    }

    public AgentModeManager? ModeManager { get; set; }

    /// <summary>The latest immutable tool snapshot available outside an active Turn.</summary>
    public EffectiveToolSnapshot? LatestToolSnapshot { get; private set; }

    public ToolLocation? ToolLocation { get; set; }

    public long NextToolSnapshotRevision() => Interlocked.Increment(ref _toolSnapshotRevision);

    public bool ToolSnapshotDirty => Volatile.Read(ref _toolSnapshotDirty) != 0;

    public void MarkToolSnapshotDirty() => Volatile.Write(ref _toolSnapshotDirty, 1);

    public void SetLatestToolSnapshot(EffectiveToolSnapshot snapshot)
    {
        LatestToolSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Volatile.Write(ref _toolSnapshotDirty, 0);
    }

    public ThreadMaintenanceState? Maintenance => Volatile.Read(ref _maintenance);

    public bool TrySetMaintenance(ThreadMaintenanceState state) =>
        Interlocked.CompareExchange(ref _maintenance, state, null) == null;

    public bool TryClearMaintenance(ThreadMaintenanceState state) =>
        Interlocked.CompareExchange(ref _maintenance, null, state) == state;

    public DateTimeOffset LastAccessAt => new(Interlocked.Read(ref _lastAccessTicks), TimeSpan.Zero);

    public void Touch() => Interlocked.Exchange(ref _lastAccessTicks, DateTimeOffset.UtcNow.UtcTicks);

    public bool GoalContinuationStarting => Volatile.Read(ref _goalContinuationStarting) != 0;

    public bool TryStartGoalContinuation() =>
        Interlocked.CompareExchange(ref _goalContinuationStarting, 1, 0) == 0;

    public void CompleteGoalContinuation() =>
        Interlocked.Exchange(ref _goalContinuationStarting, 0);

    public bool TryMarkGoalBudgetLimitReported(string goalId) =>
        _goalBudgetLimitReported.TryAdd(goalId, 0);

    public PromptRequestSnapshot? LastPromptRequest { get; set; }

    public ContextUsageComposition? ContextComposition { get; set; }

    public WorldStateSnapshot? WorldStateBaseline { get; set; }

    public WorldStateSnapshot? PersistedWorldStateBaseline { get; set; }

    public bool WorldStateBaselineRestored { get; set; }

    public string WorldStateBaselineOrigin { get; set; } = "none";

    /// <summary>The loader this thread's agent was built to read skills with, or <c>null</c> while it runs on the shared agent.</summary>
    public SkillsLoader? SkillCatalogLoader { get; set; }

    public SkillCatalogRead? SkillCatalog { get; set; }

    public ContextUsageAnchor? ContextUsageAnchor { get; set; }

    public OpaqueProviderHistorySnapshot? ResponsesProviderHistorySnapshot { get; set; }

    public bool Materialized { get; set; }

    public ConcurrentDictionary<string, TurnExecutionState> Turns { get; } = new(StringComparer.Ordinal);

    public TurnExecutionState GetOrAddTurn(string turnId) =>
        Turns.GetOrAdd(turnId, static _ => new TurnExecutionState());

    public bool TryGetTurn(string turnId, out TurnExecutionState turnRuntime) =>
        Turns.TryGetValue(turnId, out turnRuntime!);

    public bool TryRemoveTurn(string turnId, out TurnExecutionState turnRuntime) =>
        Turns.TryRemove(turnId, out turnRuntime!);

    public async ValueTask DisposeAsync()
    {
        Commands.Close();
        var turns = Turns.Values.ToArray();
        foreach (var turn in turns)
            turn.Interrupt();
        Maintenance?.Cancel();
        await Commands.DisposeAsync().ConfigureAwait(false);
        await AgentLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await ReleaseMcpAsync().ConfigureAwait(false);
        }
        finally
        {
            AgentLock.Release();
        }
        await Task.WhenAll(turns.Select(static turn => turn.ExecutionTask)).ConfigureAwait(false);
        Broker.ReleaseCurrentState(Generation);
        QueueLock.Dispose();
        AgentLock.Dispose();
        Maintenance?.Dispose();
        foreach (var turn in Turns.Values)
            turn.Dispose();
        Turns.Clear();
    }

}

/// <summary>Mutable, live-only state for one admitted Turn.</summary>
internal sealed class TurnExecutionState : IDisposable
{
    private readonly object _goalSteeringLock = new();
    private readonly Queue<string> _pendingGoalSteering = new();

    public CancellationTokenSource? Cancellation { get; set; }

    public Task ExecutionTask { get; set; } = Task.CompletedTask;

    public PersistedTurnItems PersistedItems { get; } = new();

    public bool IntentionallyInterrupted { get; private set; }

    public void Interrupt()
    {
        IntentionallyInterrupted = true;
        Cancellation?.Cancel();
    }

    /// <summary>The immutable execution choices captured when this Turn was admitted.</summary>
    public TurnExecutionContext? Context { get; set; }

    public SessionApprovalService? PendingApproval { get; set; }

    public SessionUserInputRequestService? PendingUserInput { get; set; }

    public GoalTurnSnapshot? GoalSnapshot { get; set; }

    public TokenUsageInfo LatestGoalUsage { get; set; } = new();

    /// <summary>The immutable tool registry frozen for this Turn.</summary>
    public EffectiveToolSnapshot? ToolSnapshot { get; set; }

    public ToolLocation? ToolLocation { get; set; }

    /// <summary>The canonical event channel returned by <c>SubmitInputAsync</c> for this Turn.</summary>
    public SessionEventChannel? EventChannel { get; set; }

    /// <summary>Dispatcher-owned lifecycle items keyed by the original provider call id.</summary>
    public ConcurrentDictionary<string, SessionItem> ToolInvocationItems { get; } = new(StringComparer.Ordinal);

    /// <summary>Terminal tool projections keyed by the original provider call id.</summary>
    public ConcurrentDictionary<string, byte> TerminalToolInvocations { get; } = new(StringComparer.Ordinal);

    /// <summary>Serializes streaming and dispatcher projection updates for this Turn.</summary>
    public object ToolProjectionLock { get; } = new();

    /// <summary>Allocates Session item sequence values for dispatcher-owned projections.</summary>
    public Func<int>? NextToolItemSequence { get; set; }

    public SemaphoreSlim GoalAccountingLock { get; } = new(1, 1);

    public TurnDiffTracker DiffTracker { get; } = new();

    public void EnqueueGoalSteering(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        lock (_goalSteeringLock)
            _pendingGoalSteering.Enqueue(text);
    }

    public string? TryDequeueGoalSteering()
    {
        lock (_goalSteeringLock)
            return _pendingGoalSteering.Count == 0 ? null : _pendingGoalSteering.Dequeue();
    }

    public void Dispose()
    {
        Cancellation?.Dispose();
        GoalAccountingLock.Dispose();
    }
}
