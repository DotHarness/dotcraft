using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

/// <summary>A Host terminal event, or with <see cref="StartedBy"/> a terminal that an Exec call started.</summary>
internal sealed record RemoteTerminalReport(string EventType, BackgroundTerminalSnapshot Terminal, ToolInvocationContext? StartedBy = null);

public sealed partial class RemoteExecutionSession
{
    internal event Action<RemoteExecutionSession, RemoteTerminalReport>? TerminalReported;

    internal async ValueTask<BackgroundTerminalSnapshot> StopTerminalAsync(string threadId, string sessionId,
        CancellationToken cancellationToken)
    {
        using var operation = _operations.Enter(cancellationToken);
        return await SendAsync<RemoteTerminalStopRequest, BackgroundTerminalSnapshot>(_lease.Session.Client,
            RemoteToolHostProtocol.TerminalsStop, new(Route.LeaseId, Route.WorkspaceId, threadId, sessionId),
            operation.Token).ConfigureAwait(false);
    }

    private ValueTask OnTerminalEventAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        var payload = notification.Params!.Deserialize<RemoteTerminalEvent>(RemoteToolHostProtocol.JsonOptions)!;
        TerminalReported?.Invoke(this, new(payload.EventType, payload.Terminal));
        return ValueTask.CompletedTask;
    }

    private void ReportStartedTerminals(JsonNode? node, ToolInvocationContext context)
    {
        foreach (var terminal in node?.Deserialize<BackgroundTerminalSnapshot[]>(RemoteToolHostProtocol.JsonOptions) ?? [])
            TerminalReported?.Invoke(this, new("started", terminal, context));
    }
}
