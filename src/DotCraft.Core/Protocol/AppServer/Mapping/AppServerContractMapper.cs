using System.Text.Json;
using DotCraft.Protocol.Contracts;
using Contract = DotCraft.Protocol.Contracts.AppServer;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Projects between executable contract DTOs and the existing AppServer domain wire models while
/// retaining extension properties that are not yet modeled by the initial contract slice.
/// </summary>
public static class AppServerContractMapper
{
    public static AppServerInitializeParams ToDomain(Contract.InitializeParams value) => Project<AppServerInitializeParams>(value);
    public static ThreadStartParams ToDomain(Contract.ThreadStartParams value) => Project<ThreadStartParams>(value);
    public static ThreadResumeParams ToDomain(Contract.ThreadResumeParams value) => Project<ThreadResumeParams>(value);
    public static ThreadListParams ToDomain(Contract.ThreadListParams value) => Project<ThreadListParams>(value);
    public static ThreadReadParams ToDomain(Contract.ThreadReadParams value) => Project<ThreadReadParams>(value);
    public static TurnStartParams ToDomain(Contract.TurnStartParams value) => Project<TurnStartParams>(value);
    public static TurnEnqueueParams ToDomain(Contract.TurnEnqueueParams value) => Project<TurnEnqueueParams>(value);
    public static TurnInterruptParams ToDomain(Contract.TurnInterruptParams value) => Project<TurnInterruptParams>(value);

    public static Contract.InitializeResult ToContract(AppServerInitializeResult value) => Project<Contract.InitializeResult>(value);
    public static Contract.ThreadStartResult ToContract(ThreadStartResult value) => new()
    {
        Thread = ToContract(value.Thread)
    };

    public static Contract.ThreadResumeResult ToContract(ThreadResumeResult value) => new()
    {
        Thread = ToContract(value.Thread)
    };

    public static Contract.ThreadReadResult ToContract(ThreadReadResult value) => new()
    {
        Thread = ToContract(value.Thread),
        TurnPage = value.TurnPage is null ? null : Project<Contract.ThreadReadTurnPage>(value.TurnPage)
    };
    public static Contract.ThreadListResult ToContract(ThreadListResult value) => Project<Contract.ThreadListResult>(value);
    public static Contract.TurnStartResult ToContract(TurnStartResult value) => new()
    {
        Turn = ToContract(value.Turn)
    };
    public static Contract.TurnEnqueueResult ToContract(TurnEnqueueResponse value) => Project<Contract.TurnEnqueueResult>(value);

    public static Contract.ThreadNotification ToContract(ThreadStartedNotification value) => new()
    {
        Thread = ToContract(Require(value.Thread, "thread/started"))
    };

    public static Contract.ThreadNotification ToContract(ThreadResumedNotification value) => new()
    {
        Thread = ToContract(Require(value.Thread, "thread/resumed")),
        ResumedBy = value.ResumedBy
    };

    public static Contract.ThreadNotification ToContract(ThreadUpdatedNotification value) => new()
    {
        Thread = ToContract(Require(value.Thread, "thread/updated"))
    };
    public static Contract.ThreadDeletedNotification ToContract(ThreadDeletedNotification value) => Project<Contract.ThreadDeletedNotification>(value);
    public static Contract.TurnNotification ToContract(TurnStartedNotification value) => new()
    {
        Turn = ToContract(Require(value.Turn, "turn/started"))
    };

    public static Contract.TurnNotification ToContract(TurnCompletedNotification value) => new()
    {
        Turn = ToContract(Require(value.Turn, "turn/completed"))
    };

    public static Contract.TurnNotification ToContract(TurnFailedNotification value) => new()
    {
        Turn = ToContract(Require(value.Turn, "turn/failed")),
        Error = value.Error
    };

    public static Contract.TurnNotification ToContract(TurnCancelledNotification value) => new()
    {
        Turn = ToContract(Require(value.Turn, "turn/cancelled")),
        Reason = value.Reason
    };

    public static Contract.ItemNotification ToContract(ItemStartedNotification value) =>
        ToContract(value.ThreadId, value.TurnId, Require(value.Item, "item/started"));

    public static Contract.ItemNotification ToContract(ItemCompletedNotification value) =>
        ToContract(value.ThreadId, value.TurnId, Require(value.Item, "item/completed"));

    public static Contract.ItemNotification ToContract(ApprovalResolvedNotification value) =>
        ToContract(value.ThreadId, value.TurnId, Require(value.Item, "item/approval/resolved"));

    public static Contract.ItemNotification ToContract(UserInputResolvedNotification value) =>
        ToContract(value.ThreadId, value.TurnId, Require(value.Item, "item/tool/requestUserInput/resolved"));
    public static Contract.ItemDeltaNotification ToContract(ItemDeltaNotification value) => Project<Contract.ItemDeltaNotification>(value);

    public static Contract.ApprovalRequestParams ToContract(AppServerApprovalRequestParams value) => Project<Contract.ApprovalRequestParams>(value);
    public static Contract.UserInputRequestParams ToContract(AppServerRequestUserInputParams value) => Project<Contract.UserInputRequestParams>(value);
    public static AppServerRequestUserInputResponseResult ToDomain(Contract.UserInputResponseResult value) => Project<AppServerRequestUserInputResponseResult>(value);
    public static Contract.DynamicToolCallParams ToContract(DynamicToolCallParams value) => Project<Contract.DynamicToolCallParams>(value);
    public static RuntimeDynamicToolCallResult ToDomain(Contract.DynamicToolCallResult value) => Project<RuntimeDynamicToolCallResult>(value);

    /// <summary>Projects the complete public Session Wire thread shape into Contracts.</summary>
    public static Contract.SessionThread ToContract(SessionWireThread value) => new()
    {
        Id = value.Id,
        SessionId = value.SessionId,
        WorkspacePath = value.WorkspacePath,
        Cwd = value.Cwd,
        RuntimeWorkspaceRoots = value.RuntimeWorkspaceRoots,
        EffectiveWorkspacePath = value.EffectiveWorkspacePath,
        Path = value.Path,
        ForkedFromId = value.ForkedFromId,
        ParentThreadId = value.ParentThreadId,
        Ephemeral = value.Ephemeral,
        Worktree = value.Worktree is null ? null : Project<Contract.ThreadWorktreeInfo>(value.Worktree),
        UserId = value.UserId,
        OriginChannel = value.OriginChannel,
        ChannelContext = value.ChannelContext,
        DisplayName = value.DisplayName,
        Source = Project<Contract.ThreadSource>(value.Source),
        Status = WireString(value.Status),
        CreatedAt = value.CreatedAt,
        LastActiveAt = value.LastActiveAt,
        HistoryMode = WireString(value.HistoryMode),
        Configuration = value.Configuration is null ? null : Project<Contract.ThreadConfiguration>(value.Configuration),
        Metadata = value.Metadata,
        Runtime = Project<Contract.ThreadRuntimeState>(value.Runtime),
        QueuedInputs = value.QueuedInputs.Select(static input => Project<Contract.QueuedTurnInput>(input)).ToArray(),
        Goal = value.Goal is null ? null : Project<Contract.ThreadGoalWire>(value.Goal),
        AppBindings = value.AppBindings?.Select(static binding => Project<Contract.ThreadAppBindingSummaryWire>(binding)).ToArray(),
        OriginApp = value.OriginApp is null ? null : Project<Contract.ThreadOriginAppWire>(value.OriginApp),
        OriginPresentation = value.OriginPresentation is null ? null : Project<Contract.ThreadOriginPresentationWire>(value.OriginPresentation),
        Turns = value.Turns?.Select(ToContract).ToArray(),
        Plan = value.Plan is null ? null : Project<Contract.SessionPlan>(value.Plan),
        ContextUsage = value.ContextUsage is null ? null : Project<Contract.ContextUsageSnapshot>(value.ContextUsage)
    };

    /// <summary>Projects the complete public Session Wire turn shape into Contracts.</summary>
    public static Contract.SessionTurn ToContract(SessionWireTurn value) => new()
    {
        Id = value.Id,
        ThreadId = value.ThreadId,
        Status = WireString(value.Status),
        StartedAt = value.StartedAt,
        CompletedAt = value.CompletedAt,
        TokenUsage = value.TokenUsage is null ? null : Project<Contract.TokenUsageInfo>(value.TokenUsage),
        Error = value.Error,
        OriginChannel = value.OriginChannel,
        Initiator = value.Initiator is null ? null : Project<Contract.TurnInitiatorContext>(value.Initiator),
        Items = value.Items?.Select(ToContract).ToArray()
    };

    /// <summary>Projects the complete public Session Wire item shape into Contracts.</summary>
    public static Contract.SessionItem ToContract(SessionWireItem value) => new()
    {
        Id = value.Id,
        TurnId = value.TurnId,
        Type = WireString(value.Type),
        Status = WireString(value.Status),
        CreatedAt = value.CreatedAt,
        CompletedAt = value.CompletedAt,
        PayloadKind = value.PayloadKind,
        Payload = value.Payload is null
            ? default
            : Optional<JsonElement?>.FromValue(JsonSerializer.SerializeToElement(
                value.Payload,
                value.Payload.GetType(),
                SessionWireJsonOptions.Default)),
        McpApp = value.McpApp is null ? null : Project<Contract.McpAppViewHintWire>(value.McpApp)
    };

    public static TResult ToContract<TResult>(object value) where TResult : class => Project<TResult>(value);
    public static TResult ToDomain<TResult>(object value) where TResult : class => Project<TResult>(value);

    public static object ToContract(Type contractType, object value)
    {
        var json = JsonSerializer.SerializeToElement(value, value.GetType(), GetOptions(value.GetType()));
        return json.Deserialize(contractType, GetOptions(contractType))
               ?? throw new JsonException($"Could not project AppServer contract value to {contractType.Name}.");
    }

    private static T Project<T>(object value)
    {
        var json = JsonSerializer.SerializeToElement(value, value.GetType(), GetOptions(value.GetType()));
        return json.Deserialize<T>(GetOptions(typeof(T)))
               ?? throw new JsonException($"Could not project AppServer contract value to {typeof(T).Name}.");
    }

    private static Contract.ItemNotification ToContract(string threadId, string? turnId, SessionWireItem item) => new()
    {
        ThreadId = threadId,
        TurnId = turnId,
        Item = ToContract(item)
    };

    private static T Require<T>(T? value, string method) where T : class =>
        value ?? throw new JsonException($"AppServer method '{method}' requires a {typeof(T).Name} value.");

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize wire enum {typeof(T).Name}.");

    private static JsonSerializerOptions GetOptions(Type type) =>
        type.Assembly == typeof(Contract.InitializeParams).Assembly
            ? DotCraft.Protocol.Contracts.AppServerContractJson.Options
            : SessionWireJsonOptions.Default;
}
