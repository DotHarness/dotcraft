using DotCraft.Agents;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task RecordContextHandoffAsync(
        SessionThread thread,
        SessionTurn turn,
        ChatClientAgent agent,
        SessionEventChannel events,
        Func<int> nextItemSequence)
    {
        if (turn.SampledModel is not null
            || agent.GetService<ChatClientAgentOptions>()?.MessageOrigin is not { } origin)
        {
            return;
        }

        var current = new ContextHandoffEndpoint(origin.ProviderId, origin.Model);
        ContextHandoffEndpoint? previous = null;
        await foreach (var earlier in ReadTurnsAsync(
                           thread, ThreadHistorySortDirection.Descending, includeItems: false, CancellationToken.None))
        {
            if (earlier.Id == turn.Id || earlier.SampledModel is not { } sample)
                continue;

            previous = sample.Identity;
            break;
        }

        turn.SampledModel = new(current, SessionIdGenerator.LastItemSequence(turn.Items));
        SessionItem? notice = null;
        if (previous is not null
            && (!string.Equals(previous.ProviderId, current.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previous.Model, current.Model, StringComparison.Ordinal)))
        {
            notice = CreateNoticeItem(turn, nextItemSequence(), new SystemNoticePayload
            {
                Kind = "contextHandoff",
                From = previous,
                To = current,
                FallbackText = $"Context handoff: {previous.ProviderId} / {previous.Model} → {current.ProviderId} / {current.Model}"
            });
            turn.Items.Add(notice);
        }

        try
        {
            await PersistTurnStateWithMaterializationAsync(thread, turn, CancellationToken.None);
        }
        catch
        {
            turn.SampledModel = null;
            if (notice is not null)
                turn.Items.Remove(notice);
            throw;
        }

        if (notice is not null)
        {
            events.EmitItemStarted(notice);
            events.EmitItemCompleted(notice);
        }
    }
}
