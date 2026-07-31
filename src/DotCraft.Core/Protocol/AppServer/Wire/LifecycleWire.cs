using System.Text.Json.Serialization;
using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Protocol.AppServer;

/// <summary>Shared empty JSON object used by AppServer methods with no parameters or result fields.</summary>
public sealed class RpcEmpty
{
}

/// <summary>Result for <c>thread/start</c>.</summary>
public sealed class ThreadStartResult
{
    [JsonPropertyName("thread")]
    public SessionWireThread Thread { get; set; } = new();
}

/// <summary>Result for <c>thread/fork</c>.</summary>
public sealed class ThreadForkResult
{
    [JsonPropertyName("thread")]
    public SessionWireThread Thread { get; set; } = new();
}

/// <summary>Result for <c>thread/resume</c>.</summary>
public sealed class ThreadResumeResult
{
    [JsonPropertyName("thread")]
    public SessionWireThread Thread { get; set; } = new();
}

/// <summary>Result for <c>turn/start</c>.</summary>
public sealed class TurnStartResult
{
    [JsonPropertyName("turn")]
    public SessionWireTurn Turn { get; set; } = new();
}

/// <summary>Payload for <c>thread/started</c>.</summary>
public sealed class ThreadStartedNotification
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }
}

/// <summary>Payload for <c>thread/resumed</c>.</summary>
public sealed class ThreadResumedNotification
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }

    [JsonPropertyName("resumedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumedBy { get; set; }
}

/// <summary>Payload for <c>thread/updated</c>.</summary>
public sealed class ThreadUpdatedNotification
{
    [JsonPropertyName("thread")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireThread? Thread { get; set; }
}

/// <summary>Payload for <c>thread/renamed</c>.</summary>
public sealed class ThreadRenamedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

/// <summary>Payload for <c>thread/deleted</c> and other thread-id-only notifications.</summary>
public sealed class ThreadDeletedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Payload for <c>thread/status/changed</c>.</summary>
public sealed class ThreadStatusChangedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("previousStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadStatus? PreviousStatus { get; set; }

    [JsonPropertyName("newStatus")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadStatus? NewStatus { get; set; }
}

/// <summary>Payload for <c>thread/queue/updated</c>.</summary>
public sealed class ThreadQueueUpdatedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("queuedInputs")]
    public IReadOnlyList<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

/// <summary>Payload for <c>thread/goal/updated</c>.</summary>
public sealed class ThreadGoalUpdatedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("goal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadGoalWire? Goal { get; set; }

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }
}

/// <summary>Payload for <c>thread/goal/cleared</c>.</summary>
public sealed class ThreadGoalClearedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;
}

/// <summary>Payload for <c>turn/started</c>.</summary>
public sealed class TurnStartedNotification
{
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireTurn? Turn { get; set; }
}

/// <summary>Payload for <c>turn/completed</c>.</summary>
public sealed class TurnCompletedNotification
{
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireTurn? Turn { get; set; }
}

/// <summary>Payload for <c>turn/failed</c>.</summary>
public sealed class TurnFailedNotification
{
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireTurn? Turn { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

/// <summary>Payload for <c>turn/cancelled</c>.</summary>
public sealed class TurnCancelledNotification
{
    [JsonPropertyName("turn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireTurn? Turn { get; set; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

/// <summary>Payload for <c>item/started</c>.</summary>
public sealed class ItemStartedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    [JsonPropertyName("item")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionWireItem? Item { get; set; }
}

/// <summary>Payload for <c>item/completed</c>.</summary>
public sealed class ItemCompletedNotification
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionWireItem? Item { get; set; }
}

/// <summary>Payload for <c>item/approval/resolved</c>.</summary>
public sealed class ApprovalResolvedNotification
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionWireItem? Item { get; set; }
}

/// <summary>Payload for <c>item/tool/requestUserInput/resolved</c>.</summary>
public sealed class UserInputResolvedNotification
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("item"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionWireItem? Item { get; set; }
}

/// <summary>Payload for item delta notifications.</summary>
public sealed class ItemDeltaNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    [JsonPropertyName("itemId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ItemId { get; set; }

    [JsonPropertyName("deltaKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeltaKind { get; set; }

    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; set; }

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = string.Empty;
}

/// <summary>Payload for persisted item delta event data.</summary>
public sealed class ItemDeltaPayloadWire
{
    [JsonPropertyName("deltaKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeltaKind { get; set; }

    [JsonPropertyName("toolName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolName { get; set; }

    [JsonPropertyName("callId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CallId { get; set; }

    [JsonPropertyName("delta")]
    public string Delta { get; set; } = string.Empty;
}

/// <summary>Payload for <c>subagent/progress</c>.</summary>
public sealed class SubAgentProgressNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; set; }

    [JsonPropertyName("entries")]
    public IReadOnlyList<SubAgentProgressEntry> Entries { get; set; } = [];
}

/// <summary>Payload for <c>subagent/graph/changed</c>.</summary>
public sealed class SubAgentGraphChangedNotification
{
    [JsonPropertyName("parentThreadId")]
    public string ParentThreadId { get; set; } = string.Empty;

    [JsonPropertyName("childThreadId")]
    public string ChildThreadId { get; set; } = string.Empty;
}

/// <summary>Payload for <c>item/usage/delta</c>.</summary>
public sealed class UsageDeltaNotification
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("inputTokens")] public long InputTokens { get; set; }
    [JsonPropertyName("outputTokens")] public long OutputTokens { get; set; }
    [JsonPropertyName("cachedInputTokens")] public long CachedInputTokens { get; set; }
    [JsonPropertyName("cacheWriteInputTokens")] public long CacheWriteInputTokens { get; set; }
    [JsonPropertyName("freshInputTokens")] public long FreshInputTokens { get; set; }
    [JsonPropertyName("reasoningOutputTokens")] public long ReasoningOutputTokens { get; set; }
    [JsonPropertyName("llmCallDelta")] public int LlmCallDelta { get; set; }
    [JsonPropertyName("totalInputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? TotalInputTokens { get; set; }
    [JsonPropertyName("totalOutputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? TotalOutputTokens { get; set; }
    [JsonPropertyName("contextInputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? ContextInputTokens { get; set; }
    [JsonPropertyName("turnInputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? TurnInputTokens { get; set; }
    [JsonPropertyName("turnOutputTokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? TurnOutputTokens { get; set; }
    [JsonPropertyName("turnLlmCalls"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TurnLlmCalls { get; set; }
    [JsonPropertyName("contextUsage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContextUsageSnapshot? ContextUsage { get; set; }
}

/// <summary>Payload for <c>system/event</c>.</summary>
public sealed class SystemEventNotification
{
    [JsonPropertyName("threadId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ThreadId { get; set; }
    [JsonPropertyName("turnId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? TurnId { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("messageKey"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MessageKey { get; set; }
    [JsonPropertyName("params"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyDictionary<string, object?>? Params { get; set; }
    [JsonPropertyName("fallbackText"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FallbackText { get; set; }
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Message { get; set; }
    [JsonPropertyName("percentLeft"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PercentLeft { get; set; }
    [JsonPropertyName("tokenCount"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? TokenCount { get; set; }
    [JsonPropertyName("contextUsage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ContextUsageSnapshot? ContextUsage { get; set; }
}

/// <summary>Payload for a channel rejection emitted as a <c>system/event</c>.</summary>
public sealed class ChannelRejectedSystemEventNotification
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("channelName")] public string ChannelName { get; set; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>Token usage nested in <c>system/jobResult</c>.</summary>
public sealed class SystemJobTokenUsageWire
{
    [JsonPropertyName("inputTokens")] public int InputTokens { get; set; }
    [JsonPropertyName("outputTokens")] public int OutputTokens { get; set; }
}

/// <summary>Payload for <c>system/jobResult</c>.</summary>
public sealed class SystemJobResultNotification
{
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("jobId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? JobId { get; set; }
    [JsonPropertyName("jobName"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? JobName { get; set; }
    [JsonPropertyName("threadId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ThreadId { get; set; }
    [JsonPropertyName("result"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Result { get; set; }
    [JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Error { get; set; }
    [JsonPropertyName("tokenUsage"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SystemJobTokenUsageWire? TokenUsage { get; set; }
}

/// <summary>Payload for <c>cron/stateChanged</c>.</summary>
public sealed class CronStateChangedNotification
{
    [JsonPropertyName("job")] public CronJobWireInfo Job { get; set; } = new();
    [JsonPropertyName("removed")] public bool Removed { get; set; }
}

/// <summary>Payload for MCP startup status notifications.</summary>
public sealed class McpServerStartupStatusUpdatedNotification
{
    [JsonPropertyName("threadId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ThreadId { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Error { get; set; }
    [JsonPropertyName("failureReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FailureReason { get; set; }
    [JsonPropertyName("transport"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Transport { get; set; }
    [JsonPropertyName("authStatus"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? AuthStatus { get; set; }
}

/// <summary>Payload for background-terminal lifecycle notifications.</summary>
public sealed class TerminalLifecycleNotification
{
    [JsonPropertyName("terminal")] public BackgroundTerminalSnapshot Terminal { get; set; } = null!;
    [JsonPropertyName("delta"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Delta { get; set; }
}

/// <summary>Payload for <c>plan/updated</c>.</summary>
public sealed class PlanUpdatedNotification
{
    [JsonPropertyName("threadId")] public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("overview")] public string Overview { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("todos")] public IReadOnlyList<PlanTodoWire> Todos { get; set; } = [];
}

/// <summary>Todo entry nested in <see cref="PlanUpdatedNotification"/>.</summary>
public sealed class PlanTodoWire
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("priority")] public string Priority { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
}
