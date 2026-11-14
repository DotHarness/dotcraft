namespace DotCraft.Protocol.AppServer;

public static partial class AppServerRpc
{
    public static readonly RpcRequest<ThreadTimelineListParams, ThreadTimelineListResult> ThreadTimelineList =
        new("thread/timeline/list", RpcDirection.ClientToServer, "1", Spec,
            capability: "threadManagement", scope: "thread", errors: CommonErrors);
}
