using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private McpServer? _server;
    private Task _terminalEvents = Task.CompletedTask;

    internal void AttachServer(McpServer server)
    {
        lock (_gate) _server = server;
    }

    private void PublishTerminalEvent(BackgroundTerminalEvent evt)
    {
        lock (_gate)
        {
            if (_closing is not null) return;
            var server = _server!;
            var lease = _admission!;
            var terminal = evt.EventType == BackgroundTerminalEventTypes.Completed
                ? evt.Terminal with { Output = string.Empty }
                : evt.Terminal;
            var payload = new RemoteTerminalEvent(lease.LeaseId, lease.WorkspaceId, evt.EventType, terminal);
            _terminalEvents = _terminalEvents.ContinueWith(_ => SendTerminalEventAsync(server, payload),
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task SendTerminalEventAsync(McpServer server, RemoteTerminalEvent payload)
    {
        try
        {
            await server.SendNotificationAsync(RemoteToolHostProtocol.TerminalsEvent, payload,
                RemoteToolHostProtocol.JsonOptions, CancellationToken.None).ConfigureAwait(false);
        }
        // The Agent settles terminals whose events never arrive when it learns the session ended.
        catch (Exception ex)
        {
            _host.Logger.LogDebug(ex, "Terminal event for {SessionId} was not delivered.", payload.Terminal.SessionId);
        }
    }

    private async ValueTask<JsonNode?> StopTerminalAsync(JsonRpcRequest request, string peerId, CancellationToken ct)
    {
        var input = Deserialize<RemoteTerminalStopRequest>(request);
        ValidateLease(input.LeaseId, input.WorkspaceId);
        RequirePeer(RequireState(), peerId, input.WorkspaceId);
        var threadId = ScopedThread(input.ThreadId);
        using var call = EnterThread(threadId, ct);
        HostWorkspaceRuntime? runtime;
        lock (_gate) runtime = _runtime;
        if (runtime is null || !_leaseTerminals.IsBound(input.LeaseId, input.SessionId)
            || !string.Equals((await runtime.Terminals.ReadAsync(input.SessionId, maxOutputChars: 0, ct: call.Token)
                .ConfigureAwait(false)).ThreadId, threadId, StringComparison.Ordinal))
        {
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemotePolicyDenied,
                "Only background terminals the calling thread started in this lease can be stopped.");
        }
        var stopped = await runtime.Terminals.StopAsync(input.SessionId, call.Token).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(stopped with { Output = string.Empty }, RemoteToolHostProtocol.JsonOptions);
    }
}
