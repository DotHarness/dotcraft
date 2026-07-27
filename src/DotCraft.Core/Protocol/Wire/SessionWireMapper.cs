using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using DotCraft.AppBinding;
using DotCraft.Memory;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Maps persisted Session Core models into wire DTOs.
/// </summary>
public static class SessionWireMapper
{
    public static SessionWirePlan ToWire(this StructuredPlan plan) => new()
    {
        Title = plan.Title,
        Overview = plan.Overview,
        Content = plan.Content,
        Todos = plan.Todos.Select(todo => new SessionWirePlanTodo
        {
            Id = todo.Id,
            Content = todo.Content,
            Priority = todo.Priority,
            Status = todo.Status
        }).ToList()
    };

    /// <summary>
    /// Maps a thread into the wire DTO without turn history.
    /// Equivalent to <c>thread.ToWire(includeTurns: false)</c>.
    /// The AppServer should call <c>ToWire(includeTurns: true)</c> when serving thread/read responses.
    /// </summary>
    public static SessionWireThread ToWire(this SessionThread thread) =>
        thread.ToWire(includeTurns: false);

    /// <summary>
    /// Maps a thread into the wire DTO, optionally including turn history.
    /// </summary>
    public static SessionWireThread ToWire(this SessionThread thread, bool includeTurns)
    {
        var workspace = ThreadWorkspaceResolver.Resolve(thread);
        return new()
        {
            Id = thread.Id,
            SessionId = thread.Id,
            WorkspacePath = thread.WorkspacePath,
            Cwd = workspace.Cwd,
            RuntimeWorkspaceRoots = workspace.RuntimeWorkspaceRoots,
            EffectiveWorkspacePath = workspace.Cwd,
            Path = ResolveThreadPath(thread),
            ForkedFromId = thread.ForkedFromId,
            ParentThreadId = thread.Source.SubAgent?.ParentThreadId,
            Ephemeral = thread.Ephemeral,
            Worktree = thread.Worktree,
            UserId = thread.UserId,
            OriginChannel = thread.OriginChannel,
            ChannelContext = thread.ChannelContext,
            DisplayName = thread.DisplayName,
            Source = thread.Source,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            HistoryMode = thread.HistoryMode,
            Configuration = thread.Configuration,
            Metadata = BuildThreadMetadata(thread),
            Runtime = ThreadSummaryRuntime.FromThread(thread).ToWireRuntimeState(),
            QueuedInputs = thread.QueuedInputs.ToList(),
            Turns = includeTurns ? thread.Turns.Select(t => t.ToWire(includeItems: true)).ToList() : null
        };
    }

    private static Dictionary<string, string> BuildThreadMetadata(SessionThread thread)
    {
        var metadata = new Dictionary<string, string>(thread.Metadata);
        if (!string.IsNullOrWhiteSpace(thread.Configuration?.AgentBuilderTargetId)
            && !metadata.ContainsKey(ThreadVisibility.InternalMetadataKey))
        {
            metadata[ThreadVisibility.InternalMetadataKey] = ThreadVisibility.AgentBuilderInternalValue;
        }

        return metadata;
    }

    private static string? ResolveThreadPath(SessionThread thread)
    {
        if (thread.Ephemeral || string.IsNullOrWhiteSpace(thread.WorkspacePath) || string.IsNullOrWhiteSpace(thread.Id))
            return null;

        var bucket = thread.Status == ThreadStatus.Archived ? "archived" : "active";
        var safe = string.Concat(thread.Id.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(thread.WorkspacePath, ".craft", "threads", bucket, $"{safe}.jsonl");
    }

    /// <summary>
    /// Maps a protocol runtime snapshot into the AppServer wire runtime shape.
    /// </summary>
    public static ThreadRuntimeState ToWireRuntimeState(this ThreadSummaryRuntime runtime) => new()
    {
        Running = runtime.Running,
        WaitingOnApproval = runtime.WaitingOnApproval,
        WaitingOnInput = runtime.WaitingOnInput,
        WaitingOnPlanConfirmation = runtime.WaitingOnPlanConfirmation,
        Busy = runtime.Busy,
        MaintenanceKind = runtime.MaintenanceKind
    };

    /// <summary>
    /// Maps a turn into the wire DTO without item list.
    /// </summary>
    public static SessionWireTurn ToWire(this SessionTurn turn) =>
        turn.ToWire(includeItems: false);

    /// <summary>
    /// Maps a turn into the wire DTO, optionally including items.
    /// </summary>
    public static SessionWireTurn ToWire(this SessionTurn turn, bool includeItems) =>
        new()
        {
            Id = turn.Id,
            ThreadId = turn.ThreadId,
            Status = turn.Status,
            StartedAt = turn.StartedAt,
            CompletedAt = turn.CompletedAt,
            TokenUsage = turn.TokenUsage,
            Error = turn.Error,
            OriginChannel = turn.OriginChannel,
            Initiator = turn.Initiator,
            Items = includeItems ? turn.Items.Select(i => i.ToWire()).ToList() : null
        };

    /// <summary>
    /// Maps an item into the wire DTO.
    /// </summary>
    public static SessionWireItem ToWire(this SessionItem item) =>
        new()
        {
            Id = item.Id,
            TurnId = item.TurnId,
            Type = item.Type,
            Status = item.Status,
            CreatedAt = item.CreatedAt,
            CompletedAt = item.CompletedAt,
            PayloadKind = GetPayloadKind(item.Payload),
            Payload = item.Payload
        };

    /// <summary>
    /// Returns the JSON-RPC notification method name for a given <see cref="SessionEvent"/>.
    /// The AppServer must call this to determine the <c>"method"</c> field of each outbound notification.
    ///
    /// Key mapping for item delta events (both use <see cref="SessionEventType.ItemDelta"/> internally):
    /// <list type="bullet">
    /// <item><see cref="AgentMessageDelta"/> (<c>deltaKind = "agentMessage"</c>) → <c>"item/agentMessage/delta"</c></item>
    /// <item><see cref="ReasoningContentDelta"/> (<c>deltaKind = "reasoningContent"</c>) → <c>"item/reasoning/delta"</c></item>
    /// <item><see cref="CommandExecutionOutputDelta"/> (<c>deltaKind = "commandExecution"</c>) → <c>"item/commandExecution/outputDelta"</c></item>
    /// <item><see cref="ToolCallArgumentsDelta"/> (<c>deltaKind = "toolCallArguments"</c>) → <c>"item/toolCall/argumentsDelta"</c></item>
    /// </list>
    /// All other mappings are 1:1 with the <see cref="SessionEventType"/> name converted to camelCase slash-notation.
    /// </summary>
    public static string ToWireMethodName(this SessionEvent evt) =>
        evt.EventType switch
        {
            SessionEventType.ThreadCreated => "thread/started",
            SessionEventType.ThreadResumed => "thread/resumed",
            SessionEventType.ThreadStatusChanged => "thread/statusChanged",
            SessionEventType.ThreadQueueUpdated => "thread/queue/updated",
            SessionEventType.TurnStarted => "turn/started",
            SessionEventType.TurnCompleted => "turn/completed",
            SessionEventType.TurnFailed => "turn/failed",
            SessionEventType.TurnCancelled => "turn/cancelled",
            SessionEventType.ItemStarted => "item/started",
            // ItemDelta maps to two different methods depending on payload DeltaKind
            SessionEventType.ItemDelta when evt.Payload is CommandExecutionOutputDelta => "item/commandExecution/outputDelta",
            SessionEventType.ItemDelta when evt.Payload is ReasoningContentDelta => "item/reasoning/delta",
            SessionEventType.ItemDelta when evt.Payload is ToolCallArgumentsDelta => "item/toolCall/argumentsDelta",
            SessionEventType.ItemDelta => "item/agentMessage/delta",
            SessionEventType.ItemCompleted => "item/completed",
            SessionEventType.ApprovalRequested => "item/approval/request",
            SessionEventType.ApprovalResolved => "item/approval/resolved",
            SessionEventType.UserInputRequested => "item/tool/requestUserInput",
            SessionEventType.UserInputResolved => "item/tool/requestUserInput/resolved",
            SessionEventType.SubAgentProgress => "subagent/progress",
            SessionEventType.UsageDelta => "item/usage/delta",
            SessionEventType.SystemEvent => "system/event",
            _ => evt.EventType.ToString()
        };

    /// <summary>
    /// Maps an event into the wire DTO.
    /// </summary>
    public static SessionWireEvent ToWire(this SessionEvent evt) =>
        new()
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            Timestamp = evt.Timestamp,
            PayloadKind = GetPayloadKind(evt.Payload),
            Payload = evt.Payload switch
            {
                SessionThread thread => thread.ToWire(),
                // Include items in turn notifications so clients receive the full turn state
                SessionTurn turn => turn.ToWire(includeItems: true),
                SessionItem item => item.ToWire(),
                // Map ThreadResumedPayload to wire shape: { thread, resumedBy }
                ThreadResumedPayload resumed => new { thread = resumed.Thread.ToWire(), resumedBy = resumed.ResumedBy },
                // Map TurnCancelledPayload to wire shape: { turn, reason }
                TurnCancelledPayload cancelled => new { turn = cancelled.Turn.ToWire(includeItems: true), reason = cancelled.Reason },
                // Map TurnFailedPayload to wire shape: { turn, error }
                TurnFailedPayload failed => new { turn = failed.Turn.ToWire(includeItems: true), error = failed.Error },
                // Map ThreadStatusChangedPayload to wire shape: { threadId, previousStatus, newStatus }
                ThreadStatusChangedPayload statusChanged => new { threadId = evt.ThreadId, previousStatus = statusChanged.PreviousStatus, newStatus = statusChanged.NewStatus },
                ThreadQueueUpdatedPayload queueUpdated => new { threadId = queueUpdated.ThreadId, queuedInputs = queueUpdated.QueuedInputs },
                // Flatten delta payloads to { delta } string per spec Section 6.3
                AgentMessageDelta agentDelta => new { delta = agentDelta.TextDelta },
                ReasoningContentDelta reasoningDelta => new { delta = reasoningDelta.TextDelta },
                CommandExecutionOutputDelta commandDelta => new { delta = commandDelta.TextDelta },
                ToolCallArgumentsDelta toolCallDelta => new
                {
                    deltaKind = toolCallDelta.DeltaKind,
                    toolName = toolCallDelta.ToolName,
                    callId = toolCallDelta.CallId,
                    delta = toolCallDelta.Delta
                },
                // SubAgent progress: pass through the payload as-is (entries array serialized directly)
                SubAgentProgressPayload => evt.Payload,
                // System event: pass through the payload as-is (kind + message)
                SystemEventPayload => evt.Payload,
                _ => evt.Payload
            }
        };

    /// <summary>
    /// Converts a wire input part into a <see cref="AIContent"/> for use with <see cref="ISessionService.SubmitInputAsync"/>.
    /// For <c>text</c> parts, returns <see cref="TextContent"/> directly.
    /// For <c>image</c> and <c>localImage</c> parts, returns a <see cref="TextContent"/> placeholder
    /// because <see cref="DataContent"/> requires base64-encoded <c>data:</c> URIs.
    /// The AppServer is responsible for fetching/reading image bytes and constructing proper
    /// <see cref="DataContent"/> instances before passing them to <see cref="ISessionService.SubmitInputAsync"/>.
    /// </summary>
    public static AIContent ToAIContent(this SessionWireInputPart part) =>
        part.Type switch
        {
            "text" => new TextContent(part.Text ?? string.Empty),
            "commandRef" => new TextContent(BuildCommandRefText(part)),
            "skillRef" => new TextContent(BuildSkillRefText(part)),
            "fileRef" => new TextContent(BuildFileRefText(part)),
            // image/localImage: AppServer must resolve to DataContent(bytes, mediaType) before dispatch
            "image" when part.Url is { } url => new TextContent($"[image:{url}]"),
            "localImage" when part.Path is { } path => new TextContent($"[localImage:{path}]"),
            _ => new TextContent(part.Text ?? string.Empty)
        };

    /// <summary>
    /// Builds the compatibility/display text used for user-message previews and
    /// fallback rendering from a sequence of native input parts.
    /// </summary>
    public static string BuildDisplayText(IEnumerable<SessionWireInputPart>? parts)
    {
        if (parts == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Type switch
            {
                "text" => part.Text ?? string.Empty,
                "commandRef" => BuildCommandRefText(part),
                "skillRef" => $"${part.Name?.TrimStart('/', '$') ?? string.Empty}",
                "fileRef" => $"@{(part.DisplayPath ?? part.Path ?? string.Empty)}",
                _ => string.Empty
            });
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts an <see cref="AIContent"/> into a wire input part for serialization.
    /// <see cref="DataContent"/> instances carry base64 <c>data:</c> URIs and are mapped to
    /// the <c>"image"</c> wire type with the data URI as the URL field.
    /// </summary>
    public static SessionWireInputPart ToWireInputPart(this AIContent content) =>
        content switch
        {
            TextContent tc => new SessionWireInputPart { Type = "text", Text = tc.Text },
            DataContent dc => new SessionWireInputPart { Type = "image", Url = dc.Uri },
            _ => new SessionWireInputPart { Type = "text", Text = content.ToString() }
        };

    private static string? GetPayloadKind(object? payload) =>
        payload switch
        {
            AgentMessageDelta => "agentMessageDelta",
            ReasoningContentDelta => "reasoningContentDelta",
            CommandExecutionOutputDelta => "commandExecutionOutputDelta",
            ToolCallArgumentsDelta => "toolCallArgumentsDelta",
            CommandExecutionPayload => "commandExecution",
            ToolExecutionPayload => "toolExecution",
            ImageGenerationPayload => "imageGeneration",
            ApprovalRequestPayload => "approvalRequest",
            ApprovalResponsePayload => "approvalResponse",
            UserInputRequestPayload => "userInputRequest",
            UserInputResponsePayload => "userInputResponse",
            ErrorPayload => "error",
            ToolCallPayload => "toolCall",
            McpToolCallPayload => "mcpToolCall",
            DynamicToolCallPayload => "dynamicToolCall",
            ToolResultPayload => "toolResult",
            UserMessagePayload => "userMessage",
            AgentMessagePayload => "agentMessage",
            ReasoningContentPayload => "reasoningContent",
            SystemNoticePayload => "systemNotice",
            SessionThread => "thread",
            SessionTurn => "turn",
            SessionItem => "item",
            ThreadStatusChangedPayload => "threadStatusChanged",
            ThreadResumedPayload => "threadResumed",
            ThreadQueueUpdatedPayload => "threadQueueUpdated",
            TurnCancelledPayload => "turnCancelled",
            TurnFailedPayload => "turnFailed",
            SubAgentProgressPayload => "subAgentProgress",
            SystemEventPayload => "systemEvent",
            _ => null
        };

    private static string BuildCommandRefText(SessionWireInputPart part)
    {
        if (!string.IsNullOrWhiteSpace(part.RawText))
            return part.RawText.Trim();

        var name = part.Name?.Trim().TrimStart('/', '$') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var args = part.ArgsText?.Trim();
        return string.IsNullOrWhiteSpace(args)
            ? $"/{name}"
            : $"/{name} {args}";
    }

    private static string BuildSkillRefText(SessionWireInputPart part)
    {
        var name = part.Name?.Trim().TrimStart('/') ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ? string.Empty : $"${name}";
    }

    private static string BuildFileRefText(SessionWireInputPart part)
    {
        var path = (part.DisplayPath ?? part.Path ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(path) ? string.Empty : $"@{path}";
    }
}
