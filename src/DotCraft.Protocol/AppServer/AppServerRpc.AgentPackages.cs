namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<AgentExportPlanParams, AgentExportPlanResult> AgentProfileExportPlan =
        new("agent/profiles/export/plan", RpcDirection.ClientToServer, "1", "specs/features/agent-packages.md", scope: "connection", errors: CommonErrors);

    public static readonly RpcRequest<AgentExportReadParams, AgentExportReadResult> AgentProfileExportRead =
        new("agent/profiles/export/read", RpcDirection.ClientToServer, "1", "specs/features/agent-packages.md", scope: "connection", errors: CommonErrors);

    public static readonly RpcRequest<AgentImportUploadParams, AgentImportUploadResult> AgentProfileImportUpload =
        new("agent/profiles/import/upload", RpcDirection.ClientToServer, "1", "specs/features/agent-packages.md", scope: "connection", errors: CommonErrors);

    public static readonly RpcRequest<AgentImportCommitParams, AgentProfileUpsertResult> AgentProfileImportCommit =
        new("agent/profiles/import/commit", RpcDirection.ClientToServer, "1", "specs/features/agent-packages.md", scope: "connection", errors: CommonErrors);

    public static readonly RpcRequest<AgentImportDiscardParams, RpcEmpty> AgentProfileImportDiscard =
        new("agent/profiles/import/discard", RpcDirection.ClientToServer, "1", "specs/features/agent-packages.md", scope: "connection", errors: CommonErrors);
}
