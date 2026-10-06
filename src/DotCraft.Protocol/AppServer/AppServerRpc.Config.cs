namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<ConfigReadParams, ConfigReadResult> ConfigRead =
        new("config/read", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "workspaceConfigManagement", errors: CommonErrors);

    public static readonly RpcRequest<ConfigValueWriteParams, ConfigWriteResult> ConfigValueWrite =
        new("config/value/write", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "workspaceConfigManagement", errors: CommonErrors);

    public static readonly RpcRequest<ConfigBatchWriteParams, ConfigWriteResult> ConfigBatchWrite =
        new("config/batchWrite", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "workspaceConfigManagement", errors: CommonErrors);

    public static readonly RpcRequest<ConfigSchemaParams, ConfigSchemaResult> ConfigSchema =
        new("config/schema", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", errors: CommonErrors);

    public static readonly RpcNotification<ConfigChangedParams> ConfigChanged =
        new("config/changed", RpcDirection.ServerToClient, "1", Spec, scope: "workspace", capability: "configChange", notificationOptOut: true);
}
