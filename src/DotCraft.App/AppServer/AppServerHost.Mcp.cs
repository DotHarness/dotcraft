using DotCraft.Mcp;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

public sealed partial class AppServerHost
{
    private void OnRuntimeMcpStatusChanged(McpServerStatusChangedEventArgs args)
        => BroadcastMcpStatusChanged(args.Status);

    private void BroadcastMcpStatusChanged(McpServerStatusSnapshot server)
        => BroadcastMcpStatusChanged(server, null);

    private void BroadcastThreadMcpStatusChanged(string threadId, McpServerStatusSnapshot server)
        => BroadcastMcpStatusChanged(server, threadId);

    private void BroadcastMcpStatusChanged(McpServerStatusSnapshot server, string? threadId)
    {
        var status = server.StartupState switch
        {
            "ready" => "ready",
            "starting" => "starting",
            "disabled" => "cancelled",
            _ => "failed"
        };
        var parameters = new Contract.McpServerStartupStatusUpdatedNotification
        {
            ThreadId = threadId,
            Name = server.Name,
            Status = status,
            Error = server.LastError,
            FailureReason = server.FailureReason,
            Transport = server.Transport,
            AuthStatus = server.AuthStatus
        };
        foreach (var (transport, connection) in _activeTransports)
        {
            if (connection.IsClosed
                || (threadId is not null && !connection.HasSubscription(threadId))
                || !connection.ShouldSendNotification(Contract.AppServerMethodNames.McpServerStartupStatusUpdated))
                continue;
            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.NotifyContractAsync(Contract.AppServerRpc.McpServerStartupStatusUpdated, parameters, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }
}
