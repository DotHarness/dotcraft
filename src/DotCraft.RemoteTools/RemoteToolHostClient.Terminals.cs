using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    private bool _tearingDown;

    public event Action<BackgroundTerminalEvent>? TerminalEvent;

    public IReadOnlyList<BackgroundTerminalSnapshot> ListTerminals(string threadId) =>
        [.. Sessions().SelectMany(session => session.ListTerminals(threadId))];

    public async ValueTask<BackgroundTerminalSnapshot> StopTerminalAsync(string threadId, string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = Sessions().FirstOrDefault(candidate =>
                candidate.ListTerminals(threadId).Any(terminal => terminal.SessionId == sessionId))
            ?? throw new KeyNotFoundException($"'{sessionId}' matches no background task you own.");
        return await session.StopTerminalAsync(threadId, sessionId, cancellationToken).ConfigureAwait(false);
    }

    private RemoteExecutionSession[] Sessions()
    {
        lock (_stateGate)
            return [.. _references.Keys];
    }

    private void OnTerminalEvent(BackgroundTerminalEvent notice)
    {
        lock (_stateGate)
            if (_tearingDown) return;
        TerminalEvent?.Invoke(notice);
    }
}
