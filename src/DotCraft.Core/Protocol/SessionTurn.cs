namespace DotCraft.Protocol;

/// <summary>
/// A Turn is one unit of agent work initiated by user input.
/// A Turn starts when the user submits a message and ends when the agent
/// finishes responding (or fails, or is cancelled).
/// </summary>
public sealed class SessionTurn
{
    /// <summary>
    /// Unique within the Thread. Format: turn_{3-digit-sequence} (e.g., turn_001).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Reference to the parent Thread.
    /// </summary>
    public string ThreadId { get; set; } = string.Empty;

    public TurnStatus Status { get; set; }

    /// <summary>
    /// The user's input Item that initiated this Turn. Always of type UserMessage.
    /// </summary>
    public SessionItem? Input { get; set; }

    /// <summary>
    /// All Items produced during this Turn, including the Input. Append-only.
    /// </summary>
    public List<SessionItem> Items { get; set; } = [];

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>
    /// Set when Status transitions to a terminal state (Completed, Failed, Cancelled).
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Accumulated token counts for this Turn.
    /// </summary>
    public TokenUsageInfo? TokenUsage { get; set; }

    /// <summary>
    /// Human-readable error description when Status is Failed.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Channel that originated this Turn (recorded for cross-channel resume attribution).
    /// </summary>
    public string? OriginChannel { get; set; }

    /// <summary>
    /// Durable metadata describing who initiated this turn.
    /// </summary>
    public TurnInitiatorContext? Initiator { get; set; }
}
