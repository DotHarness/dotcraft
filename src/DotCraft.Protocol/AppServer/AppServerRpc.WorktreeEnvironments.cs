namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static string[] WorktreeEnvironmentErrors =>
    [
        .. CommonErrors,
        "environmentInvalid",
        "creationCancelled",
        "cleanupFailed"
    ];

    private static string[] WorktreeHandoffErrors =>
    [
        .. WorktreeEnvironmentErrors,
        "WorktreeHandoffConflict",
        "WorktreeInUse"
    ];

    public static readonly RpcRequest<WorktreeCancelCreateParams, RpcEmpty> WorktreeCancelCreate =
        new("worktree/cancelCreate", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "gitWorktrees", errors: CommonErrors);

    public static readonly RpcRequest<WorktreeRunSetupParams, WorktreeRunSetupResult> WorktreeRunSetup =
        new("worktree/runSetup", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "gitWorktrees", errors: WorktreeEnvironmentErrors);

    public static readonly RpcRequest<WorktreeSetEnvironmentParams, WorktreeSetEnvironmentResult> WorktreeSetEnvironment =
        new("worktree/setEnvironment", RpcDirection.ClientToServer, "1", Spec, scope: "workspace", capability: "gitWorktrees", errors: WorktreeEnvironmentErrors);

    public static readonly RpcNotification<WorktreeCreateProgressNotification> WorktreeCreateProgress =
        new("worktree/createProgress", RpcDirection.ServerToClient, "1", Spec, scope: "workspace", capability: "gitWorktrees");

    public static readonly RpcNotification<WorktreeSetupOutputNotification> WorktreeSetupOutput =
        new("worktree/setupOutput", RpcDirection.ServerToClient, "1", Spec, scope: "workspace", capability: "gitWorktrees");
}
