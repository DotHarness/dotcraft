using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using ModelContextProtocol.Protocol;

namespace DotCraft.RemoteTools;

public sealed partial class RemoteExecutionSession
{
    private readonly Dictionary<string, RemoteTerminal> _terminals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<RemoteTerminalEvent>> _earlyTerminalEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _threadReleases = new(StringComparer.Ordinal);

    /// <summary>Raised with the completion and stall notices of background terminals that Exec started in this session.</summary>
    public event Action<BackgroundTerminalEvent>? TerminalEvent;

    /// <summary>Lists the background terminals a thread started in this session, without network work.</summary>
    public IReadOnlyList<BackgroundTerminalSnapshot> ListTerminals(string threadId)
    {
        lock (_stateGate)
            return [.. _terminals.Values.Where(terminal => terminal.Snapshot.ThreadId == threadId).Select(terminal => terminal.Snapshot)];
    }

    public async ValueTask<BackgroundTerminalSnapshot> StopTerminalAsync(string threadId, string sessionId,
        CancellationToken cancellationToken = default)
    {
        RemoteTerminal? terminal;
        lock (_stateGate)
        {
            if (!_terminals.TryGetValue(sessionId, out terminal) || terminal.Snapshot.ThreadId != threadId)
                throw new KeyNotFoundException($"'{sessionId}' matches no background task you own.");
            if (terminal.Snapshot.Status != BackgroundTerminalStatus.Running) return terminal.Snapshot;
        }
        using var operation = _operations.Enter(cancellationToken);
        var stopped = await SendAsync<RemoteTerminalStopRequest, BackgroundTerminalSnapshot>(_lease.Session.Client,
            RemoteToolHostProtocol.TerminalsStop, new(Route.LeaseId, Route.WorkspaceId, threadId, sessionId),
            operation.Token).ConfigureAwait(false);
        lock (_stateGate)
            return terminal.Snapshot = terminal.Owned(stopped);
    }

    private ValueTask OnTerminalEventAsync(JsonRpcNotification notification, CancellationToken cancellationToken)
    {
        var payload = notification.Params!.Deserialize<RemoteTerminalEvent>(RemoteToolHostProtocol.JsonOptions)!;
        var notices = new List<BackgroundTerminalEvent>();
        lock (_stateGate)
        {
            var id = payload.Terminal.SessionId;
            if (_terminals.TryGetValue(id, out var terminal))
                Apply(terminal, payload, notices);
            // The Exec result that lists this terminal has not arrived yet.
            else if (_earlyTerminalEvents.TryGetValue(id, out var held))
                held.Add(payload);
            else
                _earlyTerminalEvents[id] = [payload];
        }
        Publish(notices);
        return ValueTask.CompletedTask;
    }

    private int ThreadReleases(string threadId)
    {
        lock (_stateGate)
            return _threadReleases.GetValueOrDefault(threadId);
    }

    private void ReportStartedTerminals(JsonNode? node, ToolInvocationContext context, int releases)
    {
        var notices = new List<BackgroundTerminalEvent>();
        lock (_stateGate)
        {
            var released = _threadReleases.GetValueOrDefault(context.ThreadId) != releases;
            foreach (var started in node?.Deserialize<BackgroundTerminalSnapshot[]>(RemoteToolHostProtocol.JsonOptions) ?? [])
            {
                _earlyTerminalEvents.Remove(started.SessionId, out var early);
                if (!started.CompletionNoticeArmed) continue;
                var terminal = new RemoteTerminal(started with
                {
                    ThreadId = context.ThreadId,
                    TurnId = context.TurnId,
                    CallId = context.CallId
                });
                foreach (var held in early ?? [])
                    Apply(terminal, held, notices);
                if (released)
                    End(terminal, BackgroundTerminalStatus.Killed, notices);
                else
                    _terminals[started.SessionId] = terminal;
            }
        }
        Publish(notices);
        if (!IsAvailable) EndTerminals(BackgroundTerminalStatus.Lost);
    }

    private static void Apply(RemoteTerminal terminal, RemoteTerminalEvent reported, List<BackgroundTerminalEvent> notices)
    {
        var owned = terminal.Owned(reported.Terminal);
        if (reported.EventType == BackgroundTerminalEventTypes.Completed)
        {
            terminal.Snapshot = owned with { Output = string.Empty };
            return;
        }
        if (reported.EventType == BackgroundTerminalEventTypes.CompletionNotice)
        {
            terminal.Notified = true;
            terminal.Snapshot = owned with { Output = string.Empty };
        }
        notices.Add(new BackgroundTerminalEvent { EventType = reported.EventType, Terminal = owned });
    }

    private IReadOnlyCollection<string> BeginThreadRelease(string threadId)
    {
        lock (_stateGate)
        {
            _threadReleases[threadId] = _threadReleases.GetValueOrDefault(threadId) + 1;
            return [.. _terminals.Where(pair => pair.Value.Snapshot.ThreadId == threadId
                && pair.Value.Snapshot.Status == BackgroundTerminalStatus.Running).Select(pair => pair.Key)];
        }
    }

    private void ForgetThreadTerminals(string threadId)
    {
        lock (_stateGate)
            foreach (var (id, terminal) in _terminals.ToArray())
                if (terminal.Snapshot.ThreadId == threadId)
                    _terminals.Remove(id);
    }

    private void EndTerminals(string status, string? threadId = null, IReadOnlyCollection<string>? running = null)
    {
        var notices = new List<BackgroundTerminalEvent>();
        lock (_stateGate)
            foreach (var (id, terminal) in _terminals)
                if ((threadId is null || terminal.Snapshot.ThreadId == threadId)
                    && (running is null ? terminal.Snapshot.Status == BackgroundTerminalStatus.Running : running.Contains(id)))
                    End(terminal, status, notices);
        Publish(notices);
    }

    private static void End(RemoteTerminal terminal, string status, List<BackgroundTerminalEvent> notices)
    {
        if (terminal.Notified) return;
        terminal.Notified = true;
        if (terminal.Snapshot.Status == BackgroundTerminalStatus.Running)
            terminal.Snapshot = terminal.Snapshot with { Status = status, CompletedAt = DateTimeOffset.UtcNow };
        notices.Add(new BackgroundTerminalEvent { EventType = BackgroundTerminalEventTypes.CompletionNotice, Terminal = terminal.Snapshot });
    }

    private void Publish(List<BackgroundTerminalEvent> notices)
    {
        foreach (var notice in notices)
            TerminalEvent?.Invoke(notice);
    }

    private sealed class RemoteTerminal(BackgroundTerminalSnapshot snapshot)
    {
        public BackgroundTerminalSnapshot Snapshot { get; set; } = snapshot;
        public bool Notified { get; set; }

        public BackgroundTerminalSnapshot Owned(BackgroundTerminalSnapshot reported) =>
            reported with { ThreadId = Snapshot.ThreadId, TurnId = Snapshot.TurnId, CallId = Snapshot.CallId };
    }
}
