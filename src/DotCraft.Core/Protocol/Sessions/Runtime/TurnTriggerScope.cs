namespace DotCraft.Protocol;

/// <summary>
/// Metadata describing an automation-initiated turn. When present at the time
/// <see cref="ISessionService.SubmitInputAsync"/> constructs a UserMessage item,
/// the payload is annotated so clients can render a "Sent via automation"
/// affordance and route click-through.
/// </summary>
public sealed class TurnTriggerInfo
{
    /// <summary>
    /// Mechanism that synthesized this turn. Expected values include "heartbeat", "cron",
    /// "automation", "goal", and future app-specific values such as "app" or "team".
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// Optional human-readable label (e.g. cron job name, local task identifier).
    /// </summary>
    public string? Label { get; init; }

    /// <summary>
    /// Optional routing id for client-side navigation (e.g. cron job id, task id).
    /// </summary>
    public string? RefId { get; init; }
}

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed scope carrying automation trigger metadata
/// across the <c>SubmitInputAsync</c> invocation without threading it through every
/// overload. Mirrors the pattern used by <see cref="DotCraft.Channels.ChannelSessionScope"/>.
/// </summary>
public static class TurnTriggerScope
{
    private static readonly AsyncLocal<TurnTriggerInfo?> CurrentContext = new();

    public static TurnTriggerInfo? Current => CurrentContext.Value;

    public static IDisposable Set(TurnTriggerInfo info)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = info;
        return new ScopeHandle(previous);
    }

    private sealed class ScopeHandle(TurnTriggerInfo? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
