using System.Text.Json.Serialization;
using DotCraft.AppBinding;

namespace DotCraft.Protocol;

/// <summary>
/// Best-effort runtime state attached to thread summaries so reconnecting clients
/// can hydrate list activity indicators without reading every thread.
/// </summary>
public sealed class ThreadSummaryRuntime
{
    /// <summary>
    /// True when the thread currently has a running or user-waiting turn.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// True when the active turn is waiting on approval.
    /// </summary>
    public bool WaitingOnApproval { get; set; }

    /// <summary>
    /// True when the active turn is waiting on model-initiated user input.
    /// </summary>
    public bool WaitingOnInput { get; set; }

    /// <summary>
    /// True when the last completed turn produced a plan that still needs user confirmation.
    /// </summary>
    public bool WaitingOnPlanConfirmation { get; set; }

    /// <summary>
    /// True when the thread cannot start a new turn because a turn, approval, or maintenance task is active.
    /// </summary>
    public bool Busy { get; set; }

    /// <summary>
    /// Current thread maintenance kind. Null when no maintenance is active.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaintenanceKind { get; set; }

    /// <summary>
    /// Creates a runtime snapshot from persisted thread state only.
    /// Process-local maintenance state is added by <see cref="ISessionService.GetThreadRuntimeSnapshot"/>.
    /// </summary>
    public static ThreadSummaryRuntime FromThread(SessionThread thread, string? maintenanceKind = null)
    {
        var activeTurn = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
        if (activeTurn is not null)
        {
            return new ThreadSummaryRuntime
            {
                Running = true,
                WaitingOnApproval = activeTurn.Status == TurnStatus.WaitingApproval,
                WaitingOnInput = activeTurn.Status == TurnStatus.WaitingInput,
                WaitingOnPlanConfirmation = false,
                MaintenanceKind = maintenanceKind,
                Busy = true
            };
        }

        var lastTurn = thread.Turns.LastOrDefault();
        var waitingOnPlanConfirmation = lastTurn is not null
            && EndsWithSuccessfulCreatePlanInPlanMode(thread, lastTurn);
        return new ThreadSummaryRuntime
        {
            Running = false,
            WaitingOnApproval = false,
            WaitingOnInput = false,
            WaitingOnPlanConfirmation = waitingOnPlanConfirmation,
            MaintenanceKind = maintenanceKind,
            Busy = maintenanceKind is not null
        };
    }

    private static bool EndsWithSuccessfulCreatePlanInPlanMode(SessionThread thread, SessionTurn turn)
    {
        if (!string.Equals(thread.Configuration?.Mode, "plan", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var idx = turn.Items.Count - 1; idx >= 0; idx--)
        {
            if (turn.Items[idx].Payload is not ToolCallPayload toolCall)
                continue;

            if (!string.Equals(toolCall.ToolName, "CreatePlan", StringComparison.Ordinal))
                return false;

            return turn.Items
                .Where(item => item.Payload is ToolResultPayload)
                .Select(item => item.Payload as ToolResultPayload)
                .Any(result =>
                    result != null
                    && string.Equals(result.CallId, toolCall.CallId, StringComparison.Ordinal)
                    && result.Success);
        }

        return false;
    }
}

/// <summary>
/// Lightweight Thread descriptor used in the thread index and discovery results.
/// Does not include full Turn/Item history.
/// </summary>
public sealed class ThreadSummary
{
    public string Id { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string WorkspacePath { get; set; } = string.Empty;

    public string OriginChannel { get; set; } = string.Empty;

    /// <summary>
    /// Channel-specific context key (mirrors SessionThread.ChannelContext).
    /// Populated from the first-class property with a fallback to Metadata["channelContext"]
    /// for threads created before this property was introduced.
    /// </summary>
    public string? ChannelContext { get; set; }

    public string? DisplayName { get; set; }

    public ThreadSource Source { get; set; } = ThreadSource.User();

    public string? ForkedFromId { get; set; }

    public bool Ephemeral { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ThreadWorktreeInfo? Worktree { get; set; }

    public ThreadStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public int TurnCount { get; set; }

    /// <summary>
    /// Optional process-local runtime snapshot for thread-list activity indicators.
    /// Persisted index rows may omit this when the thread is not loaded in memory.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadSummaryRuntime? Runtime { get; set; }

    /// <summary>
    /// Optional current goal snapshot for list hydration.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadGoalWire? Goal { get; set; }

    /// <summary>
    /// Lightweight app binding summaries for clients that hydrate thread state from list responses.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ThreadAppBindingSummaryWire>? AppBindings { get; set; }

    /// <summary>
    /// Optional origin-app attribution: set at projection time when the thread's
    /// <see cref="OriginChannel"/> matches an installed app's declared origin channel, so clients
    /// can render the app's icon + name as the thread origin badge.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadOriginAppWire? OriginApp { get; set; }

    /// <summary>
    /// Channel-specific metadata copied from the Thread.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Creates a summary from a full SessionThread.
    /// </summary>
    public static ThreadSummary FromThread(SessionThread thread) =>
        new()
        {
            Id = thread.Id,
            UserId = thread.UserId,
            WorkspacePath = thread.WorkspacePath,
            OriginChannel = thread.OriginChannel,
            // Prefer the first-class property; fall back to Metadata for threads persisted before this field existed.
            ChannelContext = thread.ChannelContext
                ?? (thread.Metadata.TryGetValue("channelContext", out var mc) ? mc : null),
            DisplayName = thread.DisplayName,
            Source = thread.Source,
            ForkedFromId = thread.ForkedFromId,
            Ephemeral = thread.Ephemeral,
            Worktree = thread.Worktree,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            TurnCount = thread.Turns.Count,
            Runtime = ThreadSummaryRuntime.FromThread(thread),
            Metadata = BuildSummaryMetadata(thread)
        };

    private static Dictionary<string, string> BuildSummaryMetadata(SessionThread thread)
    {
        var metadata = new Dictionary<string, string>(thread.Metadata);
        // Surface the conversational Agent Builder marker so summary-based visibility checks
        // (thread/list) treat builder threads as internal even though the binding lives in the config.
        if (!string.IsNullOrWhiteSpace(thread.Configuration?.AgentBuilderTargetId)
            && !metadata.ContainsKey(ThreadVisibility.InternalMetadataKey))
        {
            metadata[ThreadVisibility.InternalMetadataKey] = ThreadVisibility.AgentBuilderInternalValue;
        }
        return metadata;
    }
}

/// <summary>
/// Origin-app attribution for a thread summary: the installed App Binding app whose declared
/// <c>originChannel</c> matches the thread's <c>OriginChannel</c>. Used by clients to render an
/// app-branded origin badge instead of the generic channel fallback.
/// </summary>
public sealed class ThreadOriginAppWire
{
    public string AppId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    /// <summary>
    /// Set when <see cref="DisplayName"/>/<see cref="Icon"/> carry a matched origin member's branding
    /// (the matched key), so clients can present a per-member rather than app-level origin.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MemberId { get; set; }
}
