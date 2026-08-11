using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.AppBinding;
using DotCraft.Memory;
using DotCraft.AppServer;

namespace DotCraft.Sessions.Wire;

/// <summary>
/// Wire-oriented JSON options for Session Protocol DTOs.
/// </summary>
public static class SessionWireJsonOptions
{
    /// <summary>
    /// The canonical options used for wire DTO serialization.
    /// </summary>
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        // Wire-spec-compliant approval decision names must be registered before the generic camelCase converter
        // so that the specific converter takes precedence for SessionApprovalDecision.
        options.Converters.Add(new WireApprovalDecisionConverter());
        options.Converters.Add(new ModelReasoningEffortJsonConverter());
        options.Converters.Add(new ReasoningOutputJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>
/// Maps <see cref="SessionApprovalDecision"/> to the wire-spec string names defined in Section 7.3
/// of the Session Wire Protocol Specification:
/// <list type="bullet">
/// <item><c>accept</c> — AcceptOnce</item>
/// <item><c>acceptForSession</c> — AcceptForSession</item>
/// <item><c>decline</c> — Reject</item>
/// <item><c>cancel</c> — CancelTurn</item>
/// </list>
/// This converter is registered in <see cref="SessionWireJsonOptions"/> only, so the persistence
/// format (SessionJsonOptions.Default) is unaffected.
/// </summary>
internal sealed class WireApprovalDecisionConverter : JsonConverter<SessionApprovalDecision>
{
    public override SessionApprovalDecision Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "accept" => SessionApprovalDecision.AcceptOnce,
            "acceptForSession" => SessionApprovalDecision.AcceptForSession,
            "acceptAlways" => SessionApprovalDecision.AcceptAlways,
            "decline" => SessionApprovalDecision.Reject,
            "cancel" => SessionApprovalDecision.CancelTurn,
            _ => throw new JsonException($"Unknown approval decision wire value: '{value}'")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        SessionApprovalDecision value,
        JsonSerializerOptions options)
    {
        var wireValue = value switch
        {
            SessionApprovalDecision.AcceptOnce => "accept",
            SessionApprovalDecision.AcceptForSession => "acceptForSession",
            SessionApprovalDecision.AcceptAlways => "acceptAlways",
            SessionApprovalDecision.Reject => "decline",
            SessionApprovalDecision.CancelTurn => "cancel",
            _ => throw new JsonException($"Unknown SessionApprovalDecision value: {value}")
        };
        writer.WriteStringValue(wireValue);
    }
}

/// <summary>
/// Advertised extension capability for wire clients.
/// </summary>
public sealed record SessionExtensionCapability
{
    public string Namespace { get; init; } = string.Empty;

    public string? Description { get; init; }
}

/// <summary>
/// Snapshot of the per-thread context-window usage used to drive the desktop token ring.
/// Tokens come from per-thread context pressure state, not billing usage.
/// </summary>
public sealed record ContextUsageSnapshot
{
    /// <summary>
    /// Tokens currently occupying the context window.
    /// This is context occupancy, not billing or cumulative turn usage.
    /// </summary>
    public long Tokens { get; init; }

    /// <summary>
    /// Raw configured context window (<c>CompactionConfig.EffectiveContextWindow()</c>).
    /// This is the denominator the desktop ring should use.
    /// </summary>
    public int ContextWindow { get; init; }

    /// <summary>
    /// Token count at which auto-compact runs (<c>CompactionConfig.AutoCompactThreshold()</c>).
    /// </summary>
    public int AutoCompactThreshold { get; init; }

    /// <summary>
    /// Token count at which <c>compactWarning</c> starts firing.
    /// </summary>
    public int WarningThreshold { get; init; }

    /// <summary>
    /// Token count at which <c>compactError</c> starts firing.
    /// </summary>
    public int ErrorThreshold { get; init; }

    /// <summary>
    /// Percent of the effective context window still available (0.0 - 1.0).
    /// </summary>
    public double PercentLeft { get; init; }

    /// <summary>
    /// Diagnostic source for the token count, such as <c>provider_context</c> or <c>estimate</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }

    /// <summary>
    /// True when the token count includes local message estimation rather than a provider-reported snapshot only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsEstimate { get; init; }
}

/// <summary>
/// Result returned by a manual thread compaction request.
/// </summary>
public sealed record ThreadCompactResult
{
    /// <summary>
    /// Lowercase compaction outcome: <c>micro</c>, <c>partial</c>, <c>skipped</c>, <c>failed</c>, or <c>cancelled</c>.
    /// </summary>
    public string Outcome { get; init; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextUsageSnapshot? ContextUsage { get; init; }
}

/// <summary>
/// Result returned by a manual thread memory consolidation request.
/// </summary>
public sealed record ThreadMemoryConsolidationResult
{
    /// <summary>
    /// Lowercase consolidation outcome: <c>succeeded</c>, <c>skipped</c>, <c>failed</c>, or <c>cancelled</c>.
    /// </summary>
    public string Outcome { get; init; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    /// <summary>
    /// Whether <c>MEMORY.md</c> was updated.
    /// </summary>
    public bool MemoryWritten { get; init; }

    /// <summary>
    /// Whether <c>HISTORY.md</c> was appended.
    /// </summary>
    public bool HistoryWritten { get; init; }
}

/// <summary>
/// Wire DTO for a thread.
/// </summary>
public sealed record SessionWireThread
{
    public string Id { get; init; } = string.Empty;

    public string SessionId { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string Cwd { get; init; } = string.Empty;

    public IReadOnlyList<string> RuntimeWorkspaceRoots { get; init; } = [];

    public string EffectiveWorkspacePath { get; init; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ForkedFromId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentThreadId { get; init; }

    public bool Ephemeral { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadWorktreeInfo? Worktree { get; init; }

    public string? UserId { get; init; }

    public string OriginChannel { get; init; } = string.Empty;

    public string? ChannelContext { get; init; }

    public string? DisplayName { get; init; }

    public ThreadSource Source { get; init; } = ThreadSource.User();

    public ThreadStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Best-effort runtime snapshot derived from persisted turns.
    /// </summary>
    public SessionRuntimeSnapshot Runtime { get; init; } = new();

    /// <summary>
    /// FIFO inputs queued behind the currently active turn.
    /// </summary>
    public List<QueuedTurnInput> QueuedInputs { get; init; } = [];

    /// <summary>
    /// Optional current goal snapshot for clients that hydrate thread state from lifecycle/list responses.
    /// </summary>
    public ThreadGoalSnapshot? Goal { get; init; }

    /// <summary>
    /// Lightweight app binding summaries for this thread. Full descriptor and audit details are exposed by app/* methods.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ThreadAppBindingSummarySnapshot>? AppBindings { get; init; }

    /// <summary>
    /// Server-resolved origin-app branding when <see cref="OriginChannel"/> matches an installed app's
    /// declared origin channel. Mirrors <c>ThreadSummary.OriginApp</c> so threads delivered via
    /// <c>thread/started</c>, <c>thread/updated</c>, <c>thread/fork</c>, and <c>thread/read</c> carry the
    /// same origin badge as <c>thread/list</c> (without it, event-delivered threads fall back to the
    /// generic channel icon).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginAppSnapshot? OriginApp { get; init; }

    /// <summary>
    /// Source-neutral origin presentation resolved by an in-process provider.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginPresentationSnapshot? OriginPresentation { get; init; }

    /// <summary>
    /// Turn summaries used by internal notifications and bounded history assembly.
    /// </summary>
    public List<SessionWireTurn>? Turns { get; init; }

    /// <summary>
    /// Persisted structured plan for this thread. Populated by <c>thread/read</c> when a plan exists.
    /// </summary>
    public SessionWirePlan? Plan { get; init; }

    /// <summary>
    /// Context usage snapshot used by the desktop token ring. Populated on
    /// <c>thread/start</c>, <c>thread/resumed</c>, and <c>thread/read</c>; null when the thread has
    /// no persisted context usage state yet.
    /// </summary>
    public ContextUsageSnapshot? ContextUsage { get; init; }
}

/// <summary>
/// Wire DTO for a turn.
/// </summary>
public sealed record SessionWireTurn
{
    public string Id { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public TurnStatus Status { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public TokenUsageInfo? TokenUsage { get; init; }

    public string? Error { get; init; }

    public string? OriginChannel { get; init; }

    public TurnInitiatorContext? Initiator { get; init; }

    /// <summary>
    /// Items produced during this turn. Populated in turn/completed and turn/started notifications.
    /// </summary>
    public List<SessionWireItem>? Items { get; init; }
}

/// <summary>
/// Wire DTO for a persisted structured plan.
/// </summary>
public sealed record SessionWirePlan
{
    public string Title { get; init; } = string.Empty;

    public string Overview { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public List<SessionWirePlanTodo> Todos { get; init; } = [];
}

/// <summary>
/// Wire DTO for a plan task.
/// </summary>
public sealed record SessionWirePlanTodo
{
    public string Id { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public string Priority { get; init; } = PlanTodoPriority.Medium;

    public string Status { get; init; } = PlanTodoStatus.Pending;
}

/// <summary>
/// Wire DTO for a session item.
/// </summary>
public sealed record SessionWireItem
{
    public string Id { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public ItemType Type { get; init; }

    public ItemStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? PayloadKind { get; init; }

    public object? Payload { get; init; }

    /// <summary>Current MCP Apps availability hint derived from the active runtime and authority.</summary>
    [JsonIgnore(
        Condition = JsonIgnoreCondition.WhenWritingNull)]
    public McpAppViewHintSnapshot? McpApp { get; init; }
}

/// <summary>Indicates that a terminal MCP item can currently open a new MCP App View.</summary>
public sealed record McpAppViewHintSnapshot
{
    public bool Available { get; init; }
}

/// <summary>
/// Wire DTO for a session event.
/// </summary>
public sealed record SessionWireEvent
{
    public string EventId { get; init; } = string.Empty;

    public SessionEventType EventType { get; init; }

    public string ThreadId { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? PayloadKind { get; init; }

    public object? Payload { get; init; }
}

/// <summary>
/// Wire DTO for a single unit of user input in a turn/start request.
/// Corresponds to the InputPart tagged union defined in Section 5.1 of the Session Wire Protocol Specification.
/// </summary>
public sealed record SessionWireInputPart
{
    /// <summary>
    /// Discriminator. One of: "text", "commandRef", "skillRef", "fileRef", "image", "localImage".
    /// </summary>
    public string Type { get; init; } = "text";

    /// <summary>
    /// Plain text content. Present when <see cref="Type"/> is "text".
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Native command or skill name. Present when <see cref="Type"/> is "commandRef" or "skillRef".
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional command arguments text. Present when <see cref="Type"/> is "commandRef".
    /// </summary>
    public string? ArgsText { get; init; }

    /// <summary>
    /// Optional raw command invocation text (for example "/review src/foo.cs").
    /// Present when <see cref="Type"/> is "commandRef".
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>
    /// Canonical file-reference path. Present when <see cref="Type"/> is "fileRef".
    /// May be a workspace-relative path or a local absolute path; outside-workspace reads remain governed by file-tool approval policy.
    /// Also used for <see cref="Type"/> "localImage".
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Optional UI-facing file-reference path. Present when <see cref="Type"/> is "fileRef".
    /// </summary>
    public string? DisplayPath { get; init; }

    /// <summary>
    /// Base64 <c>data:image/...</c> URL. Present when <see cref="Type"/> is "image".
    /// HTTP and HTTPS image URLs are not supported.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Optional local image MIME type hint. Present when <see cref="Type"/> is "localImage".
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional local image file name hint. Present when <see cref="Type"/> is "localImage".
    /// </summary>
    public string? FileName { get; init; }
}
