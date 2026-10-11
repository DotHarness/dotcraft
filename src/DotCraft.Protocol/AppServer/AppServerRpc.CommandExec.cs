namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    private static readonly string[] CommandExecErrors =
    [
        "InvalidRequest",
        "InvalidParams",
        "MethodNotFound",
        "CommandNotFound",
        "DirectoryNotFound",
        "PathBlocked"
    ];

    public static readonly RpcRequest<CommandExecParams, CommandExecResult> CommandExec =
        new("command/exec", RpcDirection.ClientToServer, "1", Spec, scope: "connection", capability: "commandExec", errors: CommandExecErrors);
}
