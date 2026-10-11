namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<GenerativeUiStateReadParams, GenerativeUiStateReadResult> GenerativeUiStateRead =
        new("generativeUi/state/read", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "generativeUi", errors: CommonErrors);

    public static readonly RpcRequest<GenerativeUiStateUpdateParams, RpcEmpty> GenerativeUiStateUpdate =
        new("generativeUi/state/update", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "generativeUi", errors: CommonErrors);

    public static readonly RpcNotification<GenerativeUiStateUpdatedNotification> GenerativeUiStateUpdated =
        new("generativeUi/state/updated", RpcDirection.ServerToClient, "1", Spec, scope: "thread", capability: "generativeUi", notificationOptOut: true);
}
