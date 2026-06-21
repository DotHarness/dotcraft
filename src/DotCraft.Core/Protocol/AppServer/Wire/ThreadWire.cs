using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


// ───── thread/start ─────

public sealed class ThreadStartParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Optional id of the thread that started this thread on the user's behalf (e.g. the
    /// Desktop CreateThread tool called from another thread). Recorded as a non-subagent
    /// origin on the new thread so its first user message can link back to the source.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SpawnedFromThreadId { get; set; }
}

// ───── thread/fork ─────

public sealed class ThreadForkParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadForkPoint? ForkPoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionIdentity? Identity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    public bool? Ephemeral { get; set; }

    public bool? ExcludeTurns { get; set; }
}

// ───── thread/resume ─────

public sealed class ThreadResumeParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicToolSpec>? DynamicTools { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, RuntimeAdditionalContextEntry>? AdditionalContext { get; set; }
}

public static class RuntimeAdditionalContextKinds
{
    public const string Application = "application";
}

public sealed class RuntimeAdditionalContextEntry
{
    public string Kind { get; set; } = RuntimeAdditionalContextKinds.Application;

    public string Value { get; set; } = string.Empty;
}

// ───── thread/list ─────

public sealed class ThreadListParams
{
    public SessionIdentity Identity { get; set; } = new();

    public bool? IncludeArchived { get; set; }

    public bool? IncludeSubAgents { get; set; }

    public bool? IncludeInternal { get; set; }

    /// <summary>
    /// When set, only threads whose <c>originChannel</c> matches (case-insensitive) are returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; set; }

    /// <summary>
    /// When non-null, passed to <see cref="ISessionService.FindThreadsAsync"/> as cross-channel origins.
    /// When null (JSON omitted), no cross-channel list is applied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CrossChannelOrigins { get; set; }

    /// <summary>
    /// Optional case-insensitive filter applied before pagination.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Query { get; set; }

    /// <summary>
    /// Optional page size. When omitted with <see cref="Cursor"/>, the server default is used.
    /// </summary>
    public int? Limit { get; set; }

    /// <summary>
    /// Opaque cursor returned by a previous <c>thread/list</c> call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; set; }
}

public sealed class ThreadListResult
{
    public List<ThreadSummary> Data { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; set; }

    public int? TotalMatched { get; set; }
}

// ───── thread/read ─────

public sealed class ThreadReadParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeTurns { get; set; }

    public int? TurnLimit { get; set; }

    /// <summary>
    /// Opaque cursor returned by a previous paged <c>thread/read</c> call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cursor { get; set; }
}

public sealed class ThreadReadResult
{
    public SessionWireThread Thread { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadReadTurnPage? TurnPage { get; set; }
}

public sealed class ThreadReadTurnPage
{
    public string Order { get; set; } = "oldestFirst";

    public int Limit { get; set; }

    public int TotalTurns { get; set; }

    public int StartOrdinal { get; set; }

    public int EndOrdinal { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; set; }

    public bool HasMore { get; set; }
}

// ───── thread/goal/* ─────

public sealed class ThreadGoalGetParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadGoalGetResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadGoalWire? Goal { get; set; }
}

public sealed class ThreadGoalSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string? Objective { get; set; }

    public string? Status { get; set; }

    public JsonElement? TokenBudget { get; set; }
}

public sealed class ThreadGoalSetResult
{
    public ThreadGoalWire Goal { get; set; } = null!;
}

public sealed class ThreadGoalClearParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadGoalClearResultWire
{
    public bool Cleared { get; set; }
}

// ───── thread/compact/start ─────

public sealed class ThreadCompactStartParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadCompactStartResponse
{
    public string Outcome { get; set; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ContextUsageSnapshot? ContextUsage { get; set; }
}

// ───── thread/memory/consolidate/start ─────

public sealed class ThreadMemoryConsolidateStartParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadMemoryConsolidateStartResponse
{
    public string Outcome { get; set; } = "skipped";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; set; }

    public bool MemoryWritten { get; set; }

    public bool HistoryWritten { get; set; }
}

// ───── thread/maintenance/interrupt ─────

public sealed class ThreadMaintenanceInterruptParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/rollback ─────

public sealed class ThreadRollbackParams
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// Number of turns to drop from the end of the thread. Must be >= 1.
    /// This only modifies conversation history and does not revert filesystem changes.
    /// </summary>
    public int NumTurns { get; set; }
}

public sealed class ThreadRollbackResponse
{
    public SessionWireThread Thread { get; set; } = new();
}

// ───── thread/subscribe ─────

public sealed class ThreadSubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? ReplayRecent { get; set; }
}

// ───── thread/unsubscribe ─────

public sealed class ThreadUnsubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/pause, archive, delete ─────

public sealed class ThreadPauseParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadArchiveParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadUnarchiveParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadDeleteParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/rename ─────

public sealed class ThreadRenameParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

// ───── thread/mode/set ─────

public sealed class ThreadModeSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;
}

// ───── thread/config/update ─────

public sealed class ThreadConfigUpdateParams
{
    public string ThreadId { get; set; } = string.Empty;

    public ThreadConfiguration Config { get; set; } = new();
}

// ───── thread/runtimeChanged (Server → Client notification) ─────

/// <summary>
/// Lightweight runtime snapshot for a thread, broadcast to all initialized connections.
/// </summary>
public sealed class ThreadRuntimeState
{
    /// <summary>
    /// True when the thread currently has a running turn.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// True when the thread currently has one or more unresolved approval requests.
    /// </summary>
    public bool WaitingOnApproval { get; set; }

    /// <summary>
    /// True when the thread currently has one or more unresolved model question requests.
    /// </summary>
    public bool WaitingOnInput { get; set; }

    /// <summary>
    /// True after a plan-mode turn ends with a successful terminal CreatePlan tool call,
    /// until the next turn starts on the same thread.
    /// </summary>
    public bool WaitingOnPlanConfirmation { get; set; }

    /// <summary>
    /// True when the thread cannot start a new turn because a turn, approval, user input, or maintenance task is active.
    /// </summary>
    public bool Busy { get; set; }

    /// <summary>
    /// Current thread maintenance kind, such as <c>compacting</c> or <c>consolidating</c>.
    /// Null when no thread maintenance is active.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaintenanceKind { get; set; }
}

/// <summary>
/// Notification payload for <c>thread/runtimeChanged</c>.
/// </summary>
public sealed class ThreadRuntimeChangedParams
{
    public string ThreadId { get; set; } = string.Empty;

    public ThreadRuntimeState Runtime { get; set; } = new();
}
