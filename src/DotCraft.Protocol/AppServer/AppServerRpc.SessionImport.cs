namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<global::DotCraft.Protocol.RpcEmpty, ImportHistoryResult> ImportHistoryList =
        new("import/history/list", RpcDirection.ClientToServer, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", errors: CommonErrors);
    private static readonly string[] SessionImportErrors = [.. CommonErrors, "import_busy"];

    public static readonly RpcRequest<ImportDetectParams, ImportDetectResult> ImportDetect =
        new("import/detect", RpcDirection.ClientToServer, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", errors: SessionImportErrors);
    public static readonly RpcRequest<ImportRunParams, ImportRunResult> ImportRun =
        new("import/run", RpcDirection.ClientToServer, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", errors: SessionImportErrors);
    public static readonly RpcRequest<global::DotCraft.Protocol.RpcEmpty, ImportSettingsResult> ImportSettingsGet =
        new("import/settings/get", RpcDirection.ClientToServer, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", errors: SessionImportErrors);
    public static readonly RpcRequest<ImportSettingsSetParams, ImportSettingsResult> ImportSettingsSet =
        new("import/settings/set", RpcDirection.ClientToServer, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", errors: SessionImportErrors);
    public static readonly RpcNotification<ImportProgressNotification> ImportProgress =
        new("import/progress", RpcDirection.ServerToClient, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", notificationOptOut: true);
    public static readonly RpcNotification<ImportCompletedNotification> ImportCompleted =
        new("import/completed", RpcDirection.ServerToClient, "1", Spec, module: "agent-import", scope: "workspace", capability: "extensions.agentImport", notificationOptOut: true);
}
