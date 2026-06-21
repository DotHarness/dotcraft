using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Protocol.AppServer;


/// <summary>
/// Params for <c>item/widget-state/set</c> (host → server, M-iv): the host persists an Interactive
/// Tool UI item's UI-only <c>widgetState</c>, keyed by <see cref="CallId"/>. Decoupled — no
/// turn/item. Null/absent <see cref="WidgetState"/> clears it. UI-only; never reaches the model.
/// </summary>
public sealed class ItemWidgetStateSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>The <c>callId</c> of the originating <c>dynamicToolCall</c> item.</summary>
    public string CallId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? WidgetState { get; set; }
}

/// <summary>Result of <c>item/widget-state/set</c>.</summary>
public sealed class ItemWidgetStateSetResult
{
    /// <summary>True when the stored state was removed (empty) rather than upserted.</summary>
    public bool Cleared { get; set; }
}

// ───── turn/start ─────

public sealed class TurnStartParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }

    /// <summary>
    /// Client-provided conversation history for HistoryMode.Client threads.
    /// Kept as raw JsonElement to defer deserialization of ChatMessage[].
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Messages { get; set; }

    /// <summary>
    /// True when this submission establishes the thread goal (the objective sent "as a goal").
    /// Persisted as <c>sentAsGoal</c> on the resulting user-message item so clients can render
    /// the "sent as goal" badge deterministically (see specs/core/goal-design.md §7.5).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; set; }
}

// ───── turn/enqueue ─────

public sealed class TurnEnqueueParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }

    /// <summary>
    /// True when this queued submission establishes the thread goal.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SentAsGoal { get; set; }
}

public sealed class TurnEnqueueResponse
{
    public QueuedTurnInput QueuedInput { get; set; } = new();

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnQueueRemoveParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string QueuedInputId { get; set; } = string.Empty;
}

public sealed class TurnQueueRemoveResponse
{
    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnQueueReorderParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<string> OrderedQueuedInputIds { get; set; } = [];
}

public sealed class TurnQueueReorderResponse
{
    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

public sealed class TurnSteerParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string ExpectedTurnId { get; set; } = string.Empty;

    public string QueuedInputId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class TurnSteerResponse
{
    public string TurnId { get; set; } = string.Empty;

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}

// ───── turn/interrupt ─────

public sealed class TurnInterruptParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}
