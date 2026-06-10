using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

internal sealed class ThreadRuntimeRegistry
{
    private readonly ConcurrentDictionary<string, ThreadRuntime> _runtimes = new(StringComparer.Ordinal);

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
}

internal sealed class ThreadRuntime(SessionThread thread) : IAsyncDisposable, IDisposable
{
    public SessionThread Thread { get; set; } = thread;

    public ThreadEventBroker Broker { get; } = new(thread.Id);

    public SemaphoreSlim QueueLock { get; set; } = new(1, 1);

    public SemaphoreSlim AgentLock { get; set; } = new(1, 1);

    public AIAgent? Agent { get; set; }

    public McpClientManager? McpManager { get; set; }

    public AgentModeManager? ModeManager { get; set; }

    public IReadOnlyList<AITool>? CurrentTools { get; set; }

    public IReadOnlySet<string>? PluginFunctionToolNames { get; set; }

    public IReadOnlySet<string>? DynamicToolNames { get; set; }

    public ThreadMaintenanceState? Maintenance { get; set; }

    public int TurnsSinceConsolidation { get; set; }

    public AutoMemoryConsolidationWork? PendingAutoMemoryConsolidation { get; set; }

    public bool ActiveAutoMemoryConsolidation { get; set; }

    public PromptRequestSnapshot? LastPromptRequest { get; set; }

    public ContextUsageAnchor? ContextUsageAnchor { get; set; }

    public bool PendingPermanentDeletion { get; set; }

    public bool Materialized { get; set; }

    public ConcurrentDictionary<string, TurnRuntime> Turns { get; } = new(StringComparer.Ordinal);

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

internal sealed class TurnRuntime
{
    public CancellationTokenSource? Cancellation { get; set; }

    public SessionApprovalService? PendingApproval { get; set; }

    public SessionUserInputRequestService? PendingUserInput { get; set; }

    public GoalTurnSnapshot? GoalSnapshot { get; set; }
}
