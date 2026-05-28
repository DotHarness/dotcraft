using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

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
        return new Scope(previous);
    }

    private sealed class Scope(TurnGuidanceRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}

/// <summary>
/// Runtime callbacks for inserting queued guidance into the current turn.
/// </summary>
public sealed class TurnGuidanceRuntimeContext
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required Func<CancellationToken, Task<ChatMessage?>> TryDrainGuidanceMessageAsync { get; init; }
}
