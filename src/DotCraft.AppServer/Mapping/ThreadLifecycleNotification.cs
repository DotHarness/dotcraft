using DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed record ThreadLifecycleNotification(string Method, object Params)
{
    public static ThreadLifecycleNotification For(string threadId, ThreadStatus newStatus) => newStatus switch
    {
        ThreadStatus.Archived => new(Contract.AppServerRpc.ThreadArchived.Name, new Contract.ThreadArchivedNotification { ThreadId = threadId }),
        _ => new(Contract.AppServerRpc.ThreadUnarchived.Name, new Contract.ThreadUnarchivedNotification { ThreadId = threadId })
    };
}
