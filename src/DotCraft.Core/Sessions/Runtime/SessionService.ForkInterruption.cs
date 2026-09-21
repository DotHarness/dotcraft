using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task<Dictionary<string, ChatMessage>> BuildForkInterruptionsAsync(
        SessionThread source, SessionThread forked, IReadOnlyList<SessionTurn> activeSourceTurns, CancellationToken ct)
    {
        var sourceHistory = source.Ephemeral
            && _runtimeRegistry.TryGetRuntime(source.Id, out var runtime)
            && runtime.EphemeralHistory is { } inMemory
                ? inMemory
                : await persistence.LoadModelHistoryAsync(source, excludedTurnId: null, ct);
        var retained = forked.Turns.ToDictionary(turn => turn.Id);
        var markers = new Dictionary<string, ChatMessage>(StringComparer.Ordinal);
        foreach (var message in sourceHistory)
        {
            if (TurnInterruption.GetTurnId(message) is not { } id
                || !retained.TryGetValue(id, out var copy))
                continue;
            var original = source.Turns.First(turn => turn.Id == id);
            if (original.Items.All(item => copy.Items.Any(candidate => candidate.Id == item.Id)))
                markers[id] = message.Clone();
        }
        if (InterruptMessageEnabled)
        {
            foreach (var original in activeSourceTurns)
            {
                if (retained.TryGetValue(original.Id, out var copy)
                    && original.Input is { } input
                    && copy.Items.Any(item => item.Type == ItemType.UserMessage && item.Id == input.Id))
                    markers.TryAdd(original.Id, TurnInterruption.Create(original.Id, ResolveThreadContextCarrier(forked)));
            }
        }
        return markers;
    }
}
