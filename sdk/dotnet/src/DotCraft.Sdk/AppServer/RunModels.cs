using DotCraft.Protocol.AppServer;

namespace DotCraft.Sdk;

/// <summary>Base streaming run event. The original notification remains available for diagnostics.</summary>
public record DotCraftRunEvent(
    string Type,
    string ThreadId,
    string? TurnId,
    AppServerNotification Raw);

/// <summary>A known AppServer notification with typed Contracts parameters.</summary>
public sealed record DotCraftRunEvent<TParams>(
    string Type,
    string ThreadId,
    string? TurnId,
    TParams Params,
    AppServerNotification Raw)
    : DotCraftRunEvent(Type, ThreadId, TurnId, Raw);

/// <summary>An unknown extension notification preserved as raw JSON.</summary>
public sealed record DotCraftRawRunEvent(
    string ThreadId,
    string? TurnId,
    AppServerNotification Raw)
    : DotCraftRunEvent(DotCraftRunEventTypes.Raw, ThreadId, TurnId, Raw)
{
    /// <summary>Raw extension parameters.</summary>
    public System.Text.Json.JsonElement Params => Raw.Params;
}

/// <summary>Stable run event type strings. Known values are the canonical AppServer method names.</summary>
public static class DotCraftRunEventTypes
{
    public const string ThreadStarted = "thread/started";
    public const string ThreadResumed = "thread/resumed";
    public const string ThreadStatusChanged = "thread/statusChanged";
    public const string ThreadRuntimeChanged = "thread/runtimeChanged";
    public const string QueueUpdated = "thread/queue/updated";
    public const string TurnStarted = "turn/started";
    public const string ItemStarted = "item/started";
    public const string ItemCompleted = "item/completed";
    public const string AgentMessageDelta = "item/agentMessage/delta";
    public const string ReasoningDelta = "item/reasoning/delta";
    public const string ToolArgumentsDelta = "item/toolCall/argumentsDelta";
    public const string ApprovalResolved = "item/approval/resolved";
    public const string UsageDelta = "item/usage/delta";
    public const string SubagentProgress = "subagent/progress";
    public const string PlanUpdated = "plan/updated";
    public const string SystemEvent = "system/event";
    public const string Completed = "turn/completed";
    public const string Failed = "turn/failed";
    public const string Cancelled = "turn/cancelled";
    public const string Raw = "raw";
}

/// <summary>Options for high-level buffered and streaming runs.</summary>
public sealed class RunOptions
{
    /// <summary>Optional per-turn sender context.</summary>
    public SenderContext? Sender { get; set; }

    /// <summary>When true, the buffered run captures every raw notification on the result.</summary>
    public bool CollectRawEvents { get; set; }

    /// <summary>When true, a busy thread enqueues the input instead of throwing.</summary>
    public bool EnqueueIfBusy { get; set; }

    /// <summary>When true (default), buffered runs throw on failed or cancelled turns.</summary>
    public bool ThrowOnFailure { get; set; } = true;
}

/// <summary>Buffered run result with the canonical merged assistant reply text.</summary>
public sealed record DotCraftRunResult(
    string ThreadId,
    string? TurnId,
    string Text,
    SessionTurn? Turn,
    IReadOnlyList<AppServerNotification>? RawEvents);

/// <summary>Common approval responses.</summary>
public static class ApprovalResponses
{
    public static ApprovalResponseResult Accept { get; } = new() { Decision = "accept" };
    public static ApprovalResponseResult AcceptForSession { get; } = new() { Decision = "acceptForSession" };
    public static ApprovalResponseResult AcceptAlways { get; } = new() { Decision = "acceptAlways" };
    public static ApprovalResponseResult Decline { get; } = new() { Decision = "decline" };
    public static ApprovalResponseResult Cancel { get; } = new() { Decision = "cancel" };
}

/// <summary>Handles a server-initiated approval request.</summary>
public delegate Task<ApprovalResponseResult> ApprovalHandler(
    ApprovalRequestParams request,
    CancellationToken cancellationToken);

/// <summary>Handles a server-initiated structured user-input request.</summary>
public delegate Task<UserInputResponseResult> UserInputHandler(
    UserInputRequestParams request,
    CancellationToken cancellationToken);
