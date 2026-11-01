namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<ThreadSearchOccurrencesParams, ThreadSearchOccurrencesResult> ThreadSearchOccurrences =
        new("thread/searchOccurrences", RpcDirection.ClientToServer, "1", Spec,
            capability: "threadManagement", scope: "thread", errors: CommonErrors);

    public static readonly RpcRequest<ThreadItemsReadParams, ThreadItemsReadResult> ThreadItemsRead =
        new("thread/items/read", RpcDirection.ClientToServer, "1", Spec,
            capability: "threadManagement", scope: "thread", errors: CommonErrors);
}
