using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ForkModelHistoryMaterialization(
    IReadOnlyList<ChatMessage> History,
    ContextCompactedPayload? Checkpoint,
    IReadOnlyList<string> WholeTurnIds,
    int WholeTurnMessageCount,
    Dictionary<string, IReadOnlyList<ChatMessage>> TurnHistories);

public sealed partial class ThreadStore
{
    internal async Task<ForkModelHistoryMaterialization> BuildForkModelHistoryMaterializationAsync(
        SessionThread source,
        SessionThread forked,
        CancellationToken ct = default,
        string? interruptedTurnId = null,
        ChatMessage? interruption = null)
    {
        var orderedTurns = OrderTurns(forked.Turns).ToArray();
        var wholeTurns = orderedTurns;
        if (orderedTurns.LastOrDefault() is { } last
            && (last.Id == interruptedTurnId
                || source.Turns.FirstOrDefault(turn => turn.Id == last.Id) is not { } original
                || !RetainsWholeForkTurn(original, last)))
        {
            wholeTurns = orderedTurns[..^1];
        }
        var wholeTurnIds = wholeTurns.Select(static turn => turn.Id).ToArray();

        var turnHistories = new Dictionary<string, IReadOnlyList<ChatMessage>>(StringComparer.Ordinal);
        var path = _rolloutStore.ResolveExistingPath(source.Id);
        var replay = path == null || wholeTurns.Length == 0
            ? null
            : await _rolloutReplayer.ReplayModelHistoryAsync(
                path,
                wholeTurnIds,
                (ids, _) => Task.FromResult<IReadOnlyList<SessionTurn>>(
                    wholeTurns.Where(turn => ids.Contains(turn.Id, StringComparer.Ordinal)).ToList()),
                ct: ct,
                expectedThreadId: source.Id,
                turnHistories: turnHistories);

        var history = replay?.Messages.ToList() ?? [];
        if (replay == null)
        {
            foreach (var turn in wholeTurns)
                AddTurn(turn);
        }
        var wholeTurnMessageCount = history.Count;
        foreach (var turn in orderedTurns.Skip(wholeTurns.Length))
            AddTurn(turn);

        return new ForkModelHistoryMaterialization(
            history, replay?.Checkpoint, wholeTurnIds, wholeTurnMessageCount, turnHistories);

        void AddTurn(SessionTurn turn)
        {
            var messages = BuildModelVisibleHistoryFromTurn(turn).ToList();
            if (turn.Id == interruptedTurnId && interruption != null)
                messages.Add(interruption);
            turnHistories[turn.Id] = messages;
            history.AddRange(messages);
        }
    }

    private static bool RetainsWholeForkTurn(SessionTurn source, SessionTurn fork)
    {
        var itemCount = source.Items.Count;
        return (fork.Items.Count == itemCount
                || fork.Items.Count == itemCount + 1 && fork.Items[^1].AsSystemNotice is { Kind: "forked" })
            && source.Items.Select(static item => item.Id)
                .SequenceEqual(fork.Items.Take(itemCount).Select(static item => item.Id));
    }
}
