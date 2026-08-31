namespace DotCraft.Sessions;

/// <summary>
/// Payload for AgentMessage items (final, after streaming).
/// </summary>
public sealed record AgentMessagePayload
{
    public string Text { get; init; } = string.Empty;

    public string? DeliveryMode { get; init; }
}

/// <summary>Payload for a runtime-managed sleep lifecycle item.</summary>
public sealed record SleepPayload
{
    public int DurationMs { get; init; }

    public long? ActualDurationMs { get; init; }

    public string Status { get; init; } = "inProgress";
}
/// <summary>
/// Delta payload emitted during AgentMessage streaming (item/delta events).
/// </summary>
public sealed record AgentMessageDelta
{
    public string DeltaKind { get; init; } = "agentMessage";

    public string TextDelta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ReasoningContent items.
/// </summary>
public sealed record ReasoningContentPayload
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Delta payload emitted during ReasoningContent streaming.
/// </summary>
public sealed record ReasoningContentDelta
{
    public string DeltaKind { get; init; } = "reasoningContent";

    public string TextDelta { get; init; } = string.Empty;
}
