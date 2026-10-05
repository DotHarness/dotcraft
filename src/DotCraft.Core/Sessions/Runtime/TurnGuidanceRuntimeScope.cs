using Microsoft.Extensions.AI;
using DotCraft.Agents;

namespace DotCraft.Sessions;

/// <summary>
/// Async-local bridge used by the steerable function-invocation loop to drain
/// same-turn user guidance at model/tool round boundaries.
/// </summary>
public static class TurnGuidanceRuntimeScope
{
    private static readonly AsyncLocal<TurnGuidanceRuntimeContext?> CurrentContext = new();

    /// <summary>
    /// Gets the active turn guidance context, if one is bound to this async flow.
    /// </summary>
    public static TurnGuidanceRuntimeContext? Current => CurrentContext.Value;

    /// <summary>
    /// Binds a guidance context for the lifetime of the returned disposable.
    /// </summary>
    public static IDisposable Set(TurnGuidanceRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        var foundationScope = StreamingGuidanceRuntimeScope.Set(new StreamingGuidanceRuntimeContext
        {
            DrainAsync = context.DrainAsync,
            HasPendingGuidanceAsync = context.HasPendingGuidanceAsync,
            WaitForInstantInterruptAsync = context.WaitForInstantInterruptAsync
        });
        var toolObserverScope = StreamingToolInvocationRuntimeScope.Set(
            new SessionStreamingToolInvocationObserver());
        return new Scope(previous, foundationScope, toolObserverScope);
    }

    private sealed class Scope(
        TurnGuidanceRuntimeContext? previous,
        IDisposable foundationScope,
        IDisposable toolObserverScope) : IDisposable
    {
        public void Dispose()
        {
            toolObserverScope.Dispose();
            foundationScope.Dispose();
            CurrentContext.Value = previous;
        }
    }
}

/// <summary>
/// Runtime callbacks for inserting same-turn guidance into the current turn
/// and observing safe model/tool-loop boundaries.
/// </summary>
public sealed class TurnGuidanceRuntimeContext
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required Func<StreamingGuidanceBoundary, CancellationToken, Task<IReadOnlyList<ChatMessage>>> DrainAsync { get; init; }

    public Func<CancellationToken, Task<bool>>? HasPendingGuidanceAsync { get; init; }

    public Func<CancellationToken, Task>? WaitForInstantInterruptAsync { get; init; }

    /// <summary>
    /// Optional callback invoked after a tool handler has actually run and produced
    /// either a normal result or a handler exception.
    /// </summary>
    public Func<string, string, CancellationToken, Task>? OnToolHandlerFinishedAsync { get; init; }
}
