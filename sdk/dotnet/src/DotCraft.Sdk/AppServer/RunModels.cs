using System.Text.Json;

namespace DotCraft.Sdk.AppServer;

/// <summary>
/// Normalized streaming run event. The original notification is preserved on <see cref="Raw"/>.
/// </summary>
public sealed record DotCraftRunEvent(
    string Type,
    string ThreadId,
    string? TurnId,
    AppServerNotification Raw)
{
    /// <summary>Raw notification parameters.</summary>
    public JsonElement Params => Raw.Params;
}

/// <summary>
/// Stable normalized run event type strings. Unmapped notifications use <see cref="Raw"/>.
/// </summary>
public static class DotCraftRunEventTypes
{
    /// <summary><c>thread/started</c>.</summary>
    public const string ThreadStarted = "thread_started";

    /// <summary><c>thread/resumed</c>.</summary>
    public const string ThreadResumed = "thread_resumed";

    /// <summary><c>thread/statusChanged</c>.</summary>
    public const string ThreadStatusChanged = "thread_status_changed";

    /// <summary><c>thread/runtimeChanged</c>.</summary>
    public const string ThreadRuntimeChanged = "thread_runtime_changed";

    /// <summary><c>thread/queue/updated</c>.</summary>
    public const string QueueUpdated = "queue_updated";

    /// <summary><c>turn/started</c>.</summary>
    public const string TurnStarted = "turn_started";

    /// <summary><c>item/started</c>.</summary>
    public const string ItemStarted = "item_started";

    /// <summary><c>item/completed</c>.</summary>
    public const string ItemCompleted = "item_completed";

    /// <summary><c>item/agentMessage/delta</c>.</summary>
    public const string AgentMessageDelta = "agent_message_delta";

    /// <summary><c>item/reasoning/delta</c>.</summary>
    public const string ReasoningDelta = "reasoning_delta";

    /// <summary><c>item/toolCall/argumentsDelta</c>.</summary>
    public const string ToolArgumentsDelta = "tool_arguments_delta";

    /// <summary><c>item/approval/resolved</c>.</summary>
    public const string ApprovalResolved = "approval_resolved";

    /// <summary><c>item/usage/delta</c>.</summary>
    public const string UsageDelta = "usage_delta";

    /// <summary><c>subagent/progress</c>.</summary>
    public const string SubagentProgress = "subagent_progress";

    /// <summary><c>plan/updated</c>.</summary>
    public const string PlanUpdated = "plan_updated";

    /// <summary><c>system/event</c>.</summary>
    public const string SystemEvent = "system_event";

    /// <summary><c>turn/completed</c>.</summary>
    public const string Completed = "completed";

    /// <summary><c>turn/failed</c>.</summary>
    public const string Failed = "failed";

    /// <summary><c>turn/cancelled</c>.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>Any subscribed notification not otherwise normalized.</summary>
    public const string Raw = "raw";
}

/// <summary>
/// Options for <see cref="DotCraftThread.RunAsync(string, RunOptions?, CancellationToken)"/> and the streaming variant.
/// </summary>
public sealed class RunOptions
{
    /// <summary>Optional per-turn sender context.</summary>
    public object? Sender { get; set; }

    /// <summary>Optional model id passthrough.</summary>
    public string? ModelId { get; set; }

    /// <summary>When true, the buffered run captures every raw notification on the result.</summary>
    public bool CollectRawEvents { get; set; }

    /// <summary>When true, a busy thread enqueues the input instead of throwing <see cref="TurnInProgressError"/>.</summary>
    public bool EnqueueIfBusy { get; set; }

    /// <summary>When true (default), <see cref="DotCraftThread.RunAsync(string, RunOptions?, CancellationToken)"/> throws on failed or cancelled turns.</summary>
    public bool ThrowOnFailure { get; set; } = true;
}

/// <summary>
/// Buffered run result with the canonical merged assistant reply text.
/// </summary>
public sealed record DotCraftRunResult(
    string ThreadId,
    string? TurnId,
    string Text,
    JsonElement? Turn,
    IReadOnlyList<AppServerNotification>? RawEvents);

/// <summary>
/// Approval decision returned by an <see cref="ApprovalHandler"/>.
/// </summary>
public sealed record ApprovalDecision(string Value)
{
    /// <summary>Approve this single call.</summary>
    public static readonly ApprovalDecision Accept = new("accept");

    /// <summary>Approve for the rest of the session.</summary>
    public static readonly ApprovalDecision AcceptForSession = new("acceptForSession");

    /// <summary>Approve and remember always.</summary>
    public static readonly ApprovalDecision AcceptAlways = new("acceptAlways");

    /// <summary>Decline this call.</summary>
    public static readonly ApprovalDecision Decline = new("decline");

    /// <summary>Cancel the turn.</summary>
    public static readonly ApprovalDecision Cancel = new("cancel");
}

/// <summary>
/// Server approval request delivered to the client.
/// </summary>
public sealed record ApprovalRequest(
    string ThreadId,
    string? TurnId,
    string? CallId,
    JsonElement Raw);

/// <summary>
/// Handles a server-initiated <c>item/approval/request</c>.
/// </summary>
public delegate Task<ApprovalDecision> ApprovalHandler(ApprovalRequest request, CancellationToken cancellationToken);

/// <summary>
/// Server user-input request delivered to the client (Plan Mode and tools).
/// </summary>
public sealed record UserInputRequest(
    string ThreadId,
    string? TurnId,
    JsonElement Raw);

/// <summary>
/// User-input answers returned by a <see cref="UserInputHandler"/>.
/// </summary>
public sealed record UserInputResponse(IReadOnlyDictionary<string, object?> Answers)
{
    /// <summary>An empty answer set, matching AppServer fallback semantics.</summary>
    public static UserInputResponse Empty { get; } = new(new Dictionary<string, object?>());
}

/// <summary>
/// Handles a server-initiated <c>item/tool/requestUserInput</c>.
/// </summary>
public delegate Task<UserInputResponse> UserInputHandler(UserInputRequest request, CancellationToken cancellationToken);

/// <summary>
/// Lightweight thread summary returned by <c>thread/list</c>.
/// </summary>
public sealed record DotCraftThreadSummary(
    string Id,
    string? Status,
    string? DisplayName,
    JsonElement Raw);
