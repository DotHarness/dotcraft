namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static string[] ThreadReadStateUpdateErrors =>
        [.. CommonErrors, "ThreadNotFound", "readStateUnavailable", "readStateConflict"];

    public static readonly RpcRequest<ThreadReadStateUpdateParams, ThreadReadStateUpdateResult> ThreadReadStateUpdate =
        new("thread/readState/update", RpcDirection.ClientToServer, "1", Spec, scope: "thread", capability: "threadManagement", errors: ThreadReadStateUpdateErrors);

    public static readonly RpcNotification<ThreadReadStateChangedNotification> ThreadReadStateChanged =
        new("thread/readState/changed", RpcDirection.ServerToClient, "1", Spec, scope: "thread", capability: "threadManagement", notificationOptOut: true);
}
