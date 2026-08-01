using System.Text.Json;
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
    public static Contract.ThreadStartResult ToContract(ThreadStartResult value) => Project<Contract.ThreadStartResult>(value);
    public static Contract.ThreadResumeResult ToContract(ThreadResumeResult value) => Project<Contract.ThreadResumeResult>(value);
    public static Contract.ThreadReadResult ToContract(ThreadReadResult value) => Project<Contract.ThreadReadResult>(value);
    public static Contract.ThreadListResult ToContract(ThreadListResult value) => Project<Contract.ThreadListResult>(value);
    public static Contract.TurnStartResult ToContract(TurnStartResult value) => Project<Contract.TurnStartResult>(value);
    public static Contract.TurnEnqueueResult ToContract(TurnEnqueueResponse value) => Project<Contract.TurnEnqueueResult>(value);

    public static Contract.ThreadNotification ToContract(ThreadStartedNotification value) => Project<Contract.ThreadNotification>(value);
    public static Contract.ThreadNotification ToContract(ThreadResumedNotification value) => Project<Contract.ThreadNotification>(value);
    public static Contract.ThreadNotification ToContract(ThreadUpdatedNotification value) => Project<Contract.ThreadNotification>(value);
    public static Contract.ThreadDeletedNotification ToContract(ThreadDeletedNotification value) => Project<Contract.ThreadDeletedNotification>(value);
    public static Contract.TurnNotification ToContract(TurnStartedNotification value) => Project<Contract.TurnNotification>(value);
    public static Contract.TurnNotification ToContract(TurnCompletedNotification value) => Project<Contract.TurnNotification>(value);
    public static Contract.TurnNotification ToContract(TurnFailedNotification value) => Project<Contract.TurnNotification>(value);
    public static Contract.TurnNotification ToContract(TurnCancelledNotification value) => Project<Contract.TurnNotification>(value);
    public static Contract.ItemNotification ToContract(ItemStartedNotification value) => Project<Contract.ItemNotification>(value);
    public static Contract.ItemNotification ToContract(ItemCompletedNotification value) => Project<Contract.ItemNotification>(value);
    public static Contract.ItemNotification ToContract(ApprovalResolvedNotification value) => Project<Contract.ItemNotification>(value);
    public static Contract.ItemNotification ToContract(UserInputResolvedNotification value) => Project<Contract.ItemNotification>(value);
    public static Contract.ItemDeltaNotification ToContract(ItemDeltaNotification value) => Project<Contract.ItemDeltaNotification>(value);

    public static Contract.ApprovalRequestParams ToContract(AppServerApprovalRequestParams value) => Project<Contract.ApprovalRequestParams>(value);
    public static Contract.UserInputRequestParams ToContract(AppServerRequestUserInputParams value) => Project<Contract.UserInputRequestParams>(value);
    public static AppServerRequestUserInputResponseResult ToDomain(Contract.UserInputResponseResult value) => Project<AppServerRequestUserInputResponseResult>(value);
    public static Contract.DynamicToolCallParams ToContract(DynamicToolCallParams value) => Project<Contract.DynamicToolCallParams>(value);
    public static RuntimeDynamicToolCallResult ToDomain(Contract.DynamicToolCallResult value) => Project<RuntimeDynamicToolCallResult>(value);

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

    private static JsonSerializerOptions GetOptions(Type type) =>
        type.Assembly == typeof(Contract.InitializeParams).Assembly
            ? DotCraft.Protocol.Contracts.AppServerContractJson.Options
            : SessionWireJsonOptions.Default;
}
