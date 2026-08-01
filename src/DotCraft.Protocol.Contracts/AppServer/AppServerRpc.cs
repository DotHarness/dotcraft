namespace DotCraft.Protocol.Contracts.AppServer;

/// <summary>Typed descriptors for the initial AppServer contract surface.</summary>
public static partial class AppServerRpc
{
    private const string Spec = "specs/protocols/appserver-protocol.md";
    private static readonly string[] CommonErrors = ["InvalidRequest", "InvalidParams", "MethodNotFound"];

    /// <summary>initialize request.</summary>
    public static readonly RpcRequest<InitializeParams, InitializeResult> Initialize =
        new("initialize", RpcDirection.ClientToServer, "1", Spec, errors: CommonErrors);

    /// <summary>initialized notification.</summary>
    public static readonly RpcNotification<RpcEmpty> Initialized =
        new("initialized", RpcDirection.ClientToServer, "1", Spec);

    /// <summary>thread/start request.</summary>
    public static readonly RpcRequest<ThreadStartParams, ThreadStartResult> ThreadStart =
        new("thread/start", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>thread/resume request.</summary>
    public static readonly RpcRequest<ThreadResumeParams, ThreadResumeResult> ThreadResume =
        new("thread/resume", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>thread/read request.</summary>
    public static readonly RpcRequest<ThreadReadParams, ThreadReadResult> ThreadRead =
        new("thread/read", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>thread/list request.</summary>
    public static readonly RpcRequest<ThreadListParams, ThreadListResult> ThreadList =
        new("thread/list", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "workspace", errors: CommonErrors);

    /// <summary>turn/start request.</summary>
    public static readonly RpcRequest<TurnStartParams, TurnStartResult> TurnStart =
        new("turn/start", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>turn/enqueue request.</summary>
    public static readonly RpcRequest<TurnEnqueueParams, TurnEnqueueResult> TurnEnqueue =
        new("turn/enqueue", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>turn/interrupt request.</summary>
    public static readonly RpcRequest<TurnInterruptParams, RpcEmpty> TurnInterrupt =
        new("turn/interrupt", RpcDirection.ClientToServer, "1", Spec, capability: "threadManagement", scope: "thread", errors: CommonErrors);

    /// <summary>thread/started notification.</summary>
    public static readonly RpcNotification<ThreadNotification> ThreadStarted =
        new("thread/started", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>thread/resumed notification.</summary>
    public static readonly RpcNotification<ThreadNotification> ThreadResumed =
        new("thread/resumed", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>thread/updated notification.</summary>
    public static readonly RpcNotification<ThreadNotification> ThreadUpdated =
        new("thread/updated", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>thread/deleted notification.</summary>
    public static readonly RpcNotification<ThreadDeletedNotification> ThreadDeleted =
        new("thread/deleted", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>turn/started notification.</summary>
    public static readonly RpcNotification<TurnNotification> TurnStarted =
        new("turn/started", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>turn/completed notification.</summary>
    public static readonly RpcNotification<TurnNotification> TurnCompleted =
        new("turn/completed", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>turn/failed notification.</summary>
    public static readonly RpcNotification<TurnNotification> TurnFailed =
        new("turn/failed", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>turn/cancelled notification.</summary>
    public static readonly RpcNotification<TurnNotification> TurnCancelled =
        new("turn/cancelled", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/started notification.</summary>
    public static readonly RpcNotification<ItemNotification> ItemStarted =
        new("item/started", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/completed notification.</summary>
    public static readonly RpcNotification<ItemNotification> ItemCompleted =
        new("item/completed", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/agentMessage/delta notification.</summary>
    public static readonly RpcNotification<ItemDeltaNotification> AgentMessageDelta =
        new("item/agentMessage/delta", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/reasoning/delta notification.</summary>
    public static readonly RpcNotification<ItemDeltaNotification> ReasoningDelta =
        new("item/reasoning/delta", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/commandExecution/outputDelta notification.</summary>
    public static readonly RpcNotification<ItemDeltaNotification> CommandOutputDelta =
        new("item/commandExecution/outputDelta", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/toolCall/argumentsDelta notification.</summary>
    public static readonly RpcNotification<ItemDeltaNotification> ToolArgumentsDelta =
        new("item/toolCall/argumentsDelta", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/approval/resolved notification.</summary>
    public static readonly RpcNotification<ItemNotification> ApprovalResolved =
        new("item/approval/resolved", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/tool/requestUserInput/resolved notification.</summary>
    public static readonly RpcNotification<ItemNotification> UserInputResolved =
        new("item/tool/requestUserInput/resolved", RpcDirection.ServerToClient, "1", Spec, scope: "thread", notificationOptOut: true);

    /// <summary>item/approval/request callback.</summary>
    public static readonly RpcRequest<ApprovalRequestParams, ApprovalResponseResult> ApprovalRequest =
        new("item/approval/request", RpcDirection.ServerToClient, "1", Spec, capability: "approvalSupport", scope: "thread", errors: CommonErrors);

    /// <summary>item/tool/requestUserInput callback.</summary>
    public static readonly RpcRequest<UserInputRequestParams, UserInputResponseResult> UserInputRequest =
        new("item/tool/requestUserInput", RpcDirection.ServerToClient, "1", Spec, capability: "requestUserInputSupport", scope: "thread", errors: CommonErrors);

    /// <summary>item/tool/call callback.</summary>
    public static readonly RpcRequest<DynamicToolCallParams, DynamicToolCallResult> DynamicToolCall =
        new("item/tool/call", RpcDirection.ServerToClient, "1", Spec, scope: "thread", errors: CommonErrors);
}
