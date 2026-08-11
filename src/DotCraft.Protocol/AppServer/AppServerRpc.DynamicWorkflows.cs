namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static readonly string[] WorkflowErrors = [.. CommonErrors, "workflow_run_not_found", "workflow_run_state_conflict", "workflow_resume_unavailable"];

    public static readonly RpcRequest<WorkflowRunListParams, WorkflowRunListResult> WorkflowRunList =
        new("workflow/run/list", RpcDirection.ClientToServer, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", errors: WorkflowErrors);
    public static readonly RpcRequest<WorkflowRunParams, WorkflowRunReadResult> WorkflowRunRead =
        new("workflow/run/read", RpcDirection.ClientToServer, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", errors: WorkflowErrors);
    public static readonly RpcRequest<WorkflowRunParams, WorkflowRunReadResult> WorkflowRunPause =
        new("workflow/run/pause", RpcDirection.ClientToServer, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", errors: WorkflowErrors);
    public static readonly RpcRequest<WorkflowRunParams, WorkflowRunReadResult> WorkflowRunStop =
        new("workflow/run/stop", RpcDirection.ClientToServer, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", errors: WorkflowErrors);
    public static readonly RpcRequest<WorkflowRunResumeParams, WorkflowRunResumeResult> WorkflowRunResume =
        new("workflow/run/resume", RpcDirection.ClientToServer, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", errors: WorkflowErrors);
    public static readonly RpcNotification<WorkflowRunUpdatedNotification> WorkflowRunUpdated =
        new("workflow/run/updated", RpcDirection.ServerToClient, "1", Spec, module: "dynamic-workflows", scope: "thread", capability: "extensions.dynamicWorkflows", notificationOptOut: true);
}
