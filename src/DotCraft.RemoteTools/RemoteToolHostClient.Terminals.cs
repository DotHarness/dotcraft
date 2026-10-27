using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    private readonly Dictionary<string, RemoteTerminal> _terminals = new(StringComparer.Ordinal);
    private readonly Dictionary<(RemoteExecutionSession Session, string Id), List<RemoteTerminalReport>> _earlyTerminalReports = [];
    private bool _tearingDown;

    public event Action<BackgroundTerminalEvent>? TerminalEvent;

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
        var stopped = await terminal.Session.StopTerminalAsync(threadId, sessionId, cancellationToken).ConfigureAwait(false);
        lock (_stateGate)
            return terminal.Snapshot = terminal.Owned(stopped);
    }

    private void OnTerminalReported(RemoteExecutionSession session, RemoteTerminalReport report)
    {
        var notices = new List<BackgroundTerminalEvent>();
        lock (_stateGate)
        {
            var id = report.Terminal.SessionId;
            if (report.StartedBy is { } context)
            {
                _earlyTerminalReports.Remove((session, id), out var early);
                if (!report.Terminal.CompletionNoticeArmed) return;
                var terminal = new RemoteTerminal(session, report.Terminal with
                {
                    ThreadId = context.ThreadId,
                    TurnId = context.TurnId,
                    CallId = context.CallId
                });
                _terminals[id] = terminal;
                foreach (var held in early ?? [])
                    Apply(terminal, held, notices);
            }
            else if (_terminals.TryGetValue(id, out var terminal))
            {
                Apply(terminal, report, notices);
            }
            else
            {
                // The Exec result that lists this terminal has not arrived yet.
                if (!_earlyTerminalReports.TryGetValue((session, id), out var held))
                    _earlyTerminalReports[(session, id)] = held = [];
                held.Add(report);
            }
        }
        Publish(notices);
        if (report.StartedBy is not null && !session.IsAvailable)
            EndTerminals(session, BackgroundTerminalStatus.Lost);
    }

    private static void Apply(RemoteTerminal terminal, RemoteTerminalReport report, List<BackgroundTerminalEvent> notices)
    {
        var owned = terminal.Owned(report.Terminal);
        if (report.EventType == BackgroundTerminalEventTypes.Completed)
        {
            terminal.Snapshot = owned with { Output = string.Empty };
            return;
        }
        if (report.EventType == BackgroundTerminalEventTypes.CompletionNotice)
        {
            terminal.Notified = true;
            terminal.Snapshot = owned with { Output = string.Empty };
        }
        notices.Add(new BackgroundTerminalEvent { EventType = report.EventType, Terminal = owned });
    }

    private IReadOnlyCollection<string> RunningTerminals(RemoteExecutionSession session, string threadId)
    {
        lock (_stateGate)
            return [.. _terminals.Where(pair => pair.Value.Session == session && pair.Value.Snapshot.ThreadId == threadId
                && pair.Value.Snapshot.Status == BackgroundTerminalStatus.Running).Select(pair => pair.Key)];
    }

    private void EndTerminals(RemoteExecutionSession session, string status, string? threadId = null,
        IReadOnlyCollection<string>? running = null)
    {
        var notices = new List<BackgroundTerminalEvent>();
        lock (_stateGate)
        {
            if (_tearingDown) return;
            foreach (var (id, terminal) in _terminals)
            {
                if (terminal.Session != session || terminal.Notified
                    || (threadId is not null && terminal.Snapshot.ThreadId != threadId)
                    || (running is null ? terminal.Snapshot.Status != BackgroundTerminalStatus.Running : !running.Contains(id)))
                    continue;
                terminal.Notified = true;
                if (terminal.Snapshot.Status == BackgroundTerminalStatus.Running)
                    terminal.Snapshot = terminal.Snapshot with { Status = status, CompletedAt = DateTimeOffset.UtcNow };
                notices.Add(new BackgroundTerminalEvent { EventType = BackgroundTerminalEventTypes.CompletionNotice, Terminal = terminal.Snapshot });
            }
        }
        Publish(notices);
    }

    private void ForgetTerminals(RemoteExecutionSession session, string? threadId)
    {
        lock (_stateGate)
        {
            foreach (var (id, terminal) in _terminals.ToArray())
                if (terminal.Session == session && (threadId is null || terminal.Snapshot.ThreadId == threadId))
                    _terminals.Remove(id);
            if (threadId is null)
                foreach (var key in _earlyTerminalReports.Keys.Where(key => key.Session == session).ToArray())
                    _earlyTerminalReports.Remove(key);
        }
    }

    private void Publish(List<BackgroundTerminalEvent> notices)
    {
        foreach (var notice in notices)
            TerminalEvent?.Invoke(notice);
    }

    private sealed class RemoteTerminal(RemoteExecutionSession session, BackgroundTerminalSnapshot snapshot)
    {
        public RemoteExecutionSession Session { get; } = session;
        public BackgroundTerminalSnapshot Snapshot { get; set; } = snapshot;
        public bool Notified { get; set; }

        public BackgroundTerminalSnapshot Owned(BackgroundTerminalSnapshot reported) =>
            reported with { ThreadId = Snapshot.ThreadId, TurnId = Snapshot.TurnId, CallId = Snapshot.CallId };
    }
}
