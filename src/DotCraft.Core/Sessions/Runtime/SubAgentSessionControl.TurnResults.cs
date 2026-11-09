namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    private static async Task<SubAgentRunResult> ReadChildTurnResultAsync(
        string childThreadId,
        IAsyncEnumerable<SessionEvent> events,
        Action<string?> started,
        CancellationToken ct)
    {
        string? turnId = null;
        var admitted = false;
        await foreach (var ev in events.WithCancellation(ct).ConfigureAwait(false))
        {
            if (ev.EventType == SessionEventType.TurnStarted && !admitted)
            {
                turnId = ev.TurnId ?? ev.TurnPayload?.Id;
                admitted = true;
                started(turnId);
            }
            if (ev.EventType is not (SessionEventType.TurnCompleted or SessionEventType.TurnCancelled or SessionEventType.TurnFailed))
                continue;
            var turn = ev.TurnPayload
                ?? throw new InvalidOperationException("Subagent terminal event did not contain a Turn result.");
            if (turnId != null && !string.Equals(turn.Id, turnId, StringComparison.Ordinal))
                continue;
            if (!admitted)
                throw new InvalidOperationException("Subagent input ended before a Turn was admitted.");
            return new SubAgentRunResult
            {
                ThreadId = childThreadId,
                Status = turn.Status.ToString().ToLowerInvariant(),
                Message = ExtractFinalAgentText(turn)
            };
        }
        throw new InvalidOperationException(admitted
            ? "Subagent event stream ended before a terminal Turn result."
            : "Subagent input ended before a Turn was admitted.");
    }
}
