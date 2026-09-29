using System.Text.Json.Nodes;
using DotCraft.Sessions;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    private readonly Dictionary<string, (string TurnId, TurnStatus Status)> _turns = new(StringComparer.Ordinal);
    private Task _turnReports = Task.CompletedTask;

    /// <summary>Queues one Turn status for the Host; reports reach it in order and repeats are dropped.</summary>
    public void ReportTurn(string threadId, string turnId, TurnStatus status)
    {
        if (!_lease.SupportsTurns) return;
        lock (_stateGate)
        {
            if (_turns.TryGetValue(threadId, out var last) && last == (turnId, status)) return;
            _turns[threadId] = (turnId, status);
            var report = new ExecutionTurnReport(Route.LeaseId, Route.WorkspaceId, threadId, turnId,
                RemoteToolHostProtocol.Wire(status));
            _turnReports = _turnReports.ContinueWith(_ => SendTurnAsync(report), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
    }

    private async Task SendTurnAsync(ExecutionTurnReport report)
    {
        using var operation = _operations.TryEnter(CancellationToken.None);
        if (operation is null || !IsAvailable) return;
        try
        {
            await SendAsync<ExecutionTurnReport, JsonObject>(_lease.Session.Client, RemoteToolHostProtocol.ExecutionThreadTurn,
                report, operation.Token).ConfigureAwait(false);
        }
        // A lost report leaves the Host a step behind until the next one; the connection reports its own loss.
        catch { }
    }
}
