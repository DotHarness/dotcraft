using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Mcp;
using DotCraft.Tools;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;

namespace DotCraft.Sessions;

internal sealed class ThreadRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, ThreadRuntime> _runtimes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _pendingPermanentDeletion = new(StringComparer.Ordinal);

    public int Count => _runtimes.Count;

    public IReadOnlyCollection<ThreadRuntime> Values => _runtimes.Values.ToArray();

    public bool TryGetRuntime(string threadId, out ThreadRuntime runtime) =>
        _runtimes.TryGetValue(threadId, out runtime!);

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

    public ThreadRuntime SetThread(SessionThread thread) =>
        _runtimes.AddOrUpdate(
            thread.Id,
            static (_, value) => new ThreadRuntime(value),
            static (_, runtime, value) =>
            {
                runtime.Thread = value;
                return runtime;
            },
            thread);

    public bool TryRemove(string threadId, out ThreadRuntime runtime) =>
        _runtimes.TryRemove(threadId, out runtime!);

    public void MarkPendingPermanentDeletion(string threadId)
    {
        _pendingPermanentDeletion[threadId] = 0;
        if (_runtimes.TryGetValue(threadId, out var runtime))
            runtime.PendingPermanentDeletion = true;
    }

    public void ClearPendingPermanentDeletion(string threadId)
    {
        _pendingPermanentDeletion.TryRemove(threadId, out _);
        if (_runtimes.TryGetValue(threadId, out var runtime))
            runtime.PendingPermanentDeletion = false;
    }

    public bool IsPendingPermanentDeletion(string threadId) =>
        _pendingPermanentDeletion.ContainsKey(threadId)
        || (_runtimes.TryGetValue(threadId, out var runtime) && runtime.PendingPermanentDeletion);
}

internal sealed class ThreadRuntime(SessionThread thread) : IAsyncDisposable, IDisposable
{
    private readonly object _bindingMcpLock = new();
    private readonly Dictionary<string, IReadOnlyList<McpServerConfig>> _bindingMcpServers =
        new(StringComparer.Ordinal);
    private ThreadMaintenanceState? _maintenance;
    private int _activeAutoMemoryConsolidation;
    private int _goalContinuationStarting;
    private int _turnsSinceConsolidation;
    private AutoMemoryConsolidationWork? _pendingAutoMemoryConsolidation;
    private readonly ConcurrentDictionary<string, byte> _goalBudgetLimitReported = new(StringComparer.Ordinal);
    private long _toolSnapshotRevision;
    private int _toolSnapshotDirty = 1;

    public SessionThread Thread { get; set; } = thread;

    public ThreadEventBroker Broker { get; } = new(thread.Id);

    public SemaphoreSlim QueueLock { get; set; } = new(1, 1);

    public SemaphoreSlim AgentLock { get; set; } = new(1, 1);

    public object TurnStartLock { get; } = new();

    public ChatClientAgent? Agent { get; set; }

    public McpClientManager? McpManager { get; set; }

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

    public long NextToolSnapshotRevision() => Interlocked.Increment(ref _toolSnapshotRevision);

    public bool ToolSnapshotDirty => Volatile.Read(ref _toolSnapshotDirty) != 0;

    public void MarkToolSnapshotDirty() => Volatile.Write(ref _toolSnapshotDirty, 1);

    public void SetLatestToolSnapshot(EffectiveToolSnapshot snapshot)
    {
        LatestToolSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Volatile.Write(ref _toolSnapshotDirty, 0);
    }

    public ThreadMaintenanceState? Maintenance => Volatile.Read(ref _maintenance);

    public int TurnsSinceConsolidation => Volatile.Read(ref _turnsSinceConsolidation);

    public bool ActiveAutoMemoryConsolidation => Volatile.Read(ref _activeAutoMemoryConsolidation) != 0;

    public bool TrySetMaintenance(ThreadMaintenanceState state) =>
        Interlocked.CompareExchange(ref _maintenance, state, null) == null;

    public bool TryClearMaintenance(ThreadMaintenanceState state) =>
        Interlocked.CompareExchange(ref _maintenance, null, state) == state;

    public int IncrementTurnsSinceConsolidation() =>
        Interlocked.Increment(ref _turnsSinceConsolidation);

    public void ResetTurnsSinceConsolidation() =>
        Interlocked.Exchange(ref _turnsSinceConsolidation, 0);

    public bool TryStartAutoMemoryConsolidation() =>
        Interlocked.CompareExchange(ref _activeAutoMemoryConsolidation, 1, 0) == 0;

    public void CompleteAutoMemoryConsolidation() =>
        Interlocked.Exchange(ref _activeAutoMemoryConsolidation, 0);

    public void SetPendingAutoMemoryConsolidation(AutoMemoryConsolidationWork work) =>
        Interlocked.Exchange(ref _pendingAutoMemoryConsolidation, work);

    public bool TryTakePendingAutoMemoryConsolidation(out AutoMemoryConsolidationWork work)
    {
        work = Interlocked.Exchange(ref _pendingAutoMemoryConsolidation, null)!;
        return work != null;
    }

    public bool TryStartGoalContinuation() =>
        Interlocked.CompareExchange(ref _goalContinuationStarting, 1, 0) == 0;

    public void CompleteGoalContinuation() =>
        Interlocked.Exchange(ref _goalContinuationStarting, 0);

    public bool TryMarkGoalBudgetLimitReported(string goalId) =>
        _goalBudgetLimitReported.TryAdd(goalId, 0);

    public PromptRequestSnapshot? LastPromptRequest { get; set; }

    public ContextUsageAnchor? ContextUsageAnchor { get; set; }

    public ProviderHistorySnapshot? ResponsesProviderHistorySnapshot { get; set; }

    public bool PendingPermanentDeletion { get; set; }

    public bool Materialized { get; set; }

    public ConcurrentDictionary<string, TurnRuntime> Turns { get; } = new(StringComparer.Ordinal);

    public TurnRuntime GetOrAddTurn(string turnId) =>
        Turns.GetOrAdd(turnId, static _ => new TurnRuntime());

    public bool TryGetTurn(string turnId, out TurnRuntime turnRuntime) =>
        Turns.TryGetValue(turnId, out turnRuntime!);

    public bool TryRemoveTurn(string turnId, out TurnRuntime turnRuntime) =>
        Turns.TryRemove(turnId, out turnRuntime!);

    public async ValueTask DisposeAsync()
    {
        QueueLock.Dispose();
        AgentLock.Dispose();
        Maintenance?.Dispose();
        if (McpManager != null)
            await McpManager.DisposeAsync();
    }

    public void Dispose()
    {
        QueueLock.Dispose();
        AgentLock.Dispose();
        Maintenance?.Dispose();
    }
}

internal sealed class TurnRuntime : IDisposable
{
    private readonly object _goalSteeringLock = new();
    private readonly Queue<string> _pendingGoalSteering = new();

    public CancellationTokenSource? Cancellation { get; set; }

    public SessionApprovalService? PendingApproval { get; set; }

    public SessionUserInputRequestService? PendingUserInput { get; set; }

    public GoalTurnSnapshot? GoalSnapshot { get; set; }

    public TokenUsageInfo LatestGoalUsage { get; set; } = new();

    /// <summary>The immutable tool registry frozen for this Turn.</summary>
    public EffectiveToolSnapshot? ToolSnapshot { get; set; }

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
