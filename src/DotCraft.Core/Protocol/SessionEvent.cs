using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

/// <summary>
/// A structured event emitted by Session Core during Thread/Turn/Item lifecycle transitions.
/// Channel adapters subscribe to these events and translate them to their transport format.
/// </summary>
public sealed class SessionEvent
{
    /// <summary>
    /// Unique event ID, monotonically increasing within a Turn.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    public SessionEventType EventType { get; set; }

    /// <summary>
    /// Parent Thread ID.
    /// </summary>
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// Parent Turn ID. Null for thread-level events (thread/created, thread/resumed, thread/statusChanged)
    /// and thread-scoped maintenance events.
    /// </summary>
    public string? TurnId { get; set; }

    /// <summary>
    /// Related Item ID. Null for turn-level and thread-level events.
    /// </summary>
    public string? ItemId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Event-type-specific payload. Use typed accessor properties for safe access.
    /// </summary>
    public object? Payload { get; set; }

    // Typed payload accessors

    [JsonIgnore]
    public SessionThread? ThreadPayload => Payload as SessionThread
        ?? (Payload as ThreadResumedPayload)?.Thread;

    [JsonIgnore]
    public SessionTurn? TurnPayload => Payload as SessionTurn
        ?? (Payload as TurnCancelledPayload)?.Turn
        ?? (Payload as TurnFailedPayload)?.Turn;

    [JsonIgnore]
    public SessionItem? ItemPayload => Payload as SessionItem;

    [JsonIgnore]
    public AgentMessageDelta? DeltaPayload => Payload as AgentMessageDelta;

    [JsonIgnore]
    public ReasoningContentDelta? ReasoningDeltaPayload => Payload as ReasoningContentDelta;

    [JsonIgnore]
    public CommandExecutionOutputDelta? CommandExecutionDeltaPayload => Payload as CommandExecutionOutputDelta;

    [JsonIgnore]
    public ToolCallArgumentsDelta? ToolCallArgumentsDeltaPayload => Payload as ToolCallArgumentsDelta;

    [JsonIgnore]
    public ThreadStatusChangedPayload? StatusChangedPayload => Payload as ThreadStatusChangedPayload;

    [JsonIgnore]
    public ThreadQueueUpdatedPayload? ThreadQueueUpdatedPayload => Payload as ThreadQueueUpdatedPayload;

    [JsonIgnore]
    public ThreadResumedPayload? ResumedPayload => Payload as ThreadResumedPayload;

    [JsonIgnore]
    public TurnCancelledPayload? TurnCancelledPayload => Payload as TurnCancelledPayload;

    [JsonIgnore]
    public TurnFailedPayload? TurnFailedPayload => Payload as TurnFailedPayload;

    [JsonIgnore]
    public SubAgentProgressPayload? SubAgentProgressPayload => Payload as SubAgentProgressPayload;

    [JsonIgnore]
    public UsageDeltaPayload? UsageDeltaPayload => Payload as UsageDeltaPayload;

    [JsonIgnore]
    public SystemEventPayload? SystemEventPayload => Payload as SystemEventPayload;
}

/// <summary>
/// Payload for turn/failed events. Carries the failed turn and the error message.
/// </summary>
public sealed record TurnFailedPayload
{
    public SessionTurn Turn { get; init; } = null!;

    /// <summary>
    /// Human-readable error message describing why the turn failed.
    /// </summary>
    public string Error { get; init; } = string.Empty;
}

/// <summary>
/// Payload for thread/statusChanged events.
/// </summary>
public sealed record ThreadStatusChangedPayload
{
    public ThreadStatus PreviousStatus { get; init; }

    public ThreadStatus NewStatus { get; init; }
}

/// <summary>
/// Thread-level queue snapshot notification payload.
/// </summary>
public sealed record ThreadQueueUpdatedPayload
{
    public string ThreadId { get; init; } = string.Empty;

    public IReadOnlyList<QueuedTurnInput> QueuedInputs { get; init; } = [];
}

/// <summary>
/// Payload for thread/resumed events. Carries the resumed thread and the channel that triggered the resume.
/// </summary>
public sealed record ThreadResumedPayload
{
    public SessionThread Thread { get; init; } = null!;

    /// <summary>
    /// Channel name of the adapter that called ResumeThreadAsync.
    /// </summary>
    public string ResumedBy { get; init; } = string.Empty;
}

/// <summary>
/// Payload for turn/cancelled events. Carries the cancelled turn and a human-readable reason.
/// </summary>
public sealed record TurnCancelledPayload
{
    public SessionTurn Turn { get; init; } = null!;

    /// <summary>
    /// Human-readable description of why the turn was cancelled.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Payload for subagent/progress events. A snapshot of all active SubAgents' real-time progress,
/// aggregated and emitted periodically (~200ms) during Turn execution.
/// </summary>
public sealed record SubAgentProgressPayload
{
    public required IReadOnlyList<SubAgentProgressEntry> Entries { get; init; }
}

/// <summary>
/// A single SubAgent's progress snapshot within a <see cref="SubAgentProgressPayload"/>.
/// </summary>
public sealed record SubAgentProgressEntry
{
    /// <summary>
    /// SubAgent identifier/label (matches the agentNickname argument passed to SpawnAgent).
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Name of the tool the SubAgent is currently executing. Null when thinking (waiting for model response).
    /// </summary>
    public string? CurrentTool { get; init; }

    /// <summary>
    /// Human-readable formatted display text for the current tool (e.g. "Read src/foo.cs lines 10-20").
    /// Null when no display formatter is registered; clients should fall back to <see cref="CurrentTool"/>.
    /// </summary>
    public string? CurrentToolDisplay { get; init; }

    /// <summary>
    /// Cumulative input token consumption.
    /// </summary>
    public long InputTokens { get; init; }

    /// <summary>
    /// Cumulative output token consumption.
    /// </summary>
    public long OutputTokens { get; init; }

    public long CachedInputTokens { get; init; }

    public long CacheWriteInputTokens { get; init; }

    public long FreshInputTokens => Math.Max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens);

    public long ReasoningOutputTokens { get; init; }

    /// <summary>
    /// Whether the SubAgent has finished execution.
    /// </summary>
    public bool IsCompleted { get; init; }
}

/// <summary>
/// Payload for usage/delta events. Carries the incremental token consumption
/// from a single LLM iteration.
/// </summary>
public sealed record UsageDeltaPayload
{
    /// <summary>
    /// Input tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long InputTokens { get; init; }

    /// <summary>
    /// Output tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long OutputTokens { get; init; }

    /// <summary>
    /// Cache-hit input tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long CachedInputTokens { get; init; }

    /// <summary>
    /// Cache-write input tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long CacheWriteInputTokens { get; init; }

    /// <summary>
    /// Fresh input tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long FreshInputTokens => Math.Max(0, InputTokens - CachedInputTokens - CacheWriteInputTokens);

    /// <summary>
    /// Reasoning output tokens consumed in this LLM iteration (delta, not cumulative).
    /// </summary>
    public long ReasoningOutputTokens { get; init; }

    /// <summary>
    /// 1 when this delta starts a new LLM request, otherwise 0.
    /// </summary>
    public int LlmCallDelta { get; init; }

    /// <summary>
    /// Optional persisted context-occupancy input-token snapshot for the thread
    /// at the time of the delta. Desktop clients use this to drive the
    /// context-usage ring without waiting for turn completion.
    /// This is an alias of <see cref="ContextInputTokens"/> for compatibility,
    /// not billing-cumulative input usage.
    /// </summary>
    public long? TotalInputTokens { get; init; }

    /// <summary>
    /// Optional persisted context-occupancy input-token snapshot for the thread.
    /// This is current context window occupancy, not billing-cumulative usage.
    /// </summary>
    public long? ContextInputTokens { get; init; }

    /// <summary>
    /// Optional cumulative billing input tokens emitted so far in the current turn.
    /// </summary>
    public long? TurnInputTokens { get; init; }

    /// <summary>
    /// Optional cumulative output-token total emitted so far in the current
    /// turn. Populated when possible; null otherwise. This is not used for
    /// context-occupancy calculations.
    /// </summary>
    public long? TotalOutputTokens { get; init; }

    /// <summary>
    /// Optional cumulative billing output tokens emitted so far in the current turn.
    /// </summary>
    public long? TurnOutputTokens { get; init; }

    /// <summary>
    /// Optional cumulative LLM request count emitted so far in the current turn.
    /// </summary>
    public int? TurnLlmCalls { get; init; }

    /// <summary>
    /// Optional full context-usage snapshot matching <see cref="TotalInputTokens"/>.
    /// Clients can use this to seed context-window thresholds from a live usage
    /// event when they have not received a thread snapshot yet.
    /// </summary>
    public ContextUsageSnapshot? ContextUsage { get; init; }
}

/// <summary>
/// Payload for system/event events. Carries information about system-level maintenance
/// operations (context compaction, memory consolidation) that occur during a Turn's
/// post-processing phase.
/// </summary>
public sealed record SystemEventPayload
{
    /// <summary>
    /// System event kind. One of: "compactWarning", "compactError",
    /// "compacting", "compacted", "compactSkipped", "compactFailed",
    /// "streamError", "consolidating", "consolidated", "consolidationSkipped",
    /// "consolidationFailed".
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>
    /// Optional human-readable message describing the operation.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Fraction of the effective context window still available (0.0 – 1.0).
    /// Populated for compact threshold/warning/error/success events so UIs can
    /// render a usage bar without recomputing thresholds.
    /// </summary>
    public double? PercentLeft { get; init; }

    /// <summary>
    /// Estimated input tokens at the time the event was emitted (after
    /// compaction for <c>compacted</c> events, before for warnings).
    /// </summary>
    public long? TokenCount { get; init; }

    /// <summary>
    /// Optional full context-usage snapshot. Compaction terminal events use this
    /// so clients can refresh context-window UI even when no prior snapshot has
    /// been seeded locally.
    /// </summary>
    public ContextUsageSnapshot? ContextUsage { get; init; }
}
