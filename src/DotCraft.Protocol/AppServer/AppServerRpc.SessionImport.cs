namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static readonly string[] SessionImportErrors = [.. CommonErrors, "import_busy", "import_source_unavailable"];

    public static readonly RpcRequest<ImportSessionsDetectParams, ImportSessionsDetectResult> ImportSessionsDetect =
        new("import/sessions/detect", RpcDirection.ClientToServer, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", errors: SessionImportErrors);
    public static readonly RpcRequest<ImportSessionsRunParams, ImportSessionsRunResult> ImportSessionsRun =
        new("import/sessions/run", RpcDirection.ClientToServer, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", errors: SessionImportErrors);
    public static readonly RpcRequest<global::DotCraft.Protocol.RpcEmpty, ImportSettingsResult> ImportSettingsGet =
        new("import/settings/get", RpcDirection.ClientToServer, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", errors: SessionImportErrors);
    public static readonly RpcRequest<ImportSettingsSetParams, ImportSettingsResult> ImportSettingsSet =
        new("import/settings/set", RpcDirection.ClientToServer, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", errors: SessionImportErrors);
    public static readonly RpcNotification<ImportSessionsProgressNotification> ImportSessionsProgress =
        new("import/sessions/progress", RpcDirection.ServerToClient, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", notificationOptOut: true);
    public static readonly RpcNotification<ImportSessionsCompletedNotification> ImportSessionsCompleted =
        new("import/sessions/completed", RpcDirection.ServerToClient, "1", Spec, module: "session-import", scope: "workspace", capability: "extensions.sessionImport", notificationOptOut: true);
}
