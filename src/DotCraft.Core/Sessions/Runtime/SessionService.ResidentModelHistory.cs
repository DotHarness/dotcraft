using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ResidentModelHistory(
    long Revision,
    CoveredTurnSet Turns,
    IReadOnlyList<ModelHistoryMessage> Messages);

internal readonly record struct CoveredTurnSet(int Count, string? LastTurnId);

public sealed partial class SessionService
{
    private sealed record ResidentHistoryBaseline(
        long Revision,
        IReadOnlyList<ModelHistoryMessage> Messages,
        ChatMessage[] SessionPrefix);

    private async Task<(List<ChatMessage> History, ResidentHistoryBaseline? Baseline)> LoadServerModelHistoryAsync(
        SessionThread thread,
        string? excludedTurnId,
        CancellationToken ct)
    {
        if (thread.Ephemeral)
            return (await persistence.LoadModelHistoryAsync(thread, excludedTurnId, ct), null);

        var coveredTurns = CoveredTurns(thread, excludedTurnId);
        var runtime = TryGetCurrentRuntime(thread);
        if (runtime?.ResidentModelHistory is { } resident
            && resident.Revision == persistence.GetModelHistoryRevision(thread.Id).Value
            && resident.Turns == coveredTurns)
        {
            var history = ThreadStore.DecodeModelHistory(resident.Messages);
            return (history, new ResidentHistoryBaseline(resident.Revision, resident.Messages, [.. history]));
        }

        var load = await persistence.LoadResidentModelHistoryAsync(thread, excludedTurnId, ct);
        if (!load.Residable)
            return (load.Messages, null);

        var encoded = ThreadStore.EncodeModelHistory(load.Messages);
        if (runtime != null)
        {
            runtime.ResidentModelHistory = coveredTurns is { } covered
                ? new ResidentModelHistory(load.Revision.Value, covered, encoded)
                : null;
        }
        return (load.Messages, new ResidentHistoryBaseline(load.Revision.Value, encoded, [.. load.Messages]));
    }

    private void UpdateResidentModelHistoryAfterCommit(
        SessionThread thread,
        ResidentHistoryBaseline? baseline,
        IReadOnlyList<ChatMessage>? session,
        ModelHistoryRevision? committed)
    {
        if (thread.Ephemeral || TryGetCurrentRuntime(thread) is not { } runtime)
            return;

        if (baseline == null
            || session == null
            || committed is not { } revision
            || revision.Value <= baseline.Revision
            || revision.LastFailedWrite > baseline.Revision)
        {
            runtime.ResidentModelHistory = null;
            return;
        }

        IReadOnlyList<ModelHistoryMessage> messages;
        if (revision.LastCheckpoint > baseline.Revision)
        {
            messages = ThreadStore.EncodeModelHistory(session);
        }
        else
        {
            var prefix = baseline.SessionPrefix;
            if (session.Count < prefix.Length
                || prefix.Where((message, index) => !ReferenceEquals(message, session[index])).Any())
            {
                runtime.ResidentModelHistory = null;
                return;
            }

            messages = [.. baseline.Messages, .. ThreadStore.EncodeModelHistory(session.Skip(prefix.Length))];
        }

        runtime.ResidentModelHistory = CoveredTurns(thread, excludedTurnId: null) is { } covered
            ? new ResidentModelHistory(revision.Value, covered, messages)
            : null;
    }

    private void InstallResidentModelHistoryReplacement(
        SessionThread thread,
        string coveredTurnId,
        IReadOnlyList<ChatMessage> replacement)
    {
        if (thread.Ephemeral || TryGetCurrentRuntime(thread) is not { } runtime)
            return;

        var revision = persistence.GetModelHistoryRevision(thread.Id);
        runtime.ResidentModelHistory = CoveredTurns(thread, excludedTurnId: null) is { } covered
            && string.Equals(covered.LastTurnId, coveredTurnId, StringComparison.Ordinal)
            && revision.LastCheckpoint == revision.Value
                ? new ResidentModelHistory(revision.Value, covered, ThreadStore.EncodeModelHistory(replacement))
                : null;
    }

    private ThreadRuntime? TryGetCurrentRuntime(SessionThread thread) =>
        _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime) && ReferenceEquals(runtime.Thread, thread)
            ? runtime
            : null;

    private static CoveredTurnSet? CoveredTurns(SessionThread thread, string? excludedTurnId)
    {
        var turns = thread.Turns;
        var count = thread.TurnCount;
        var last = turns.Count - 1;
        if (last >= 0 && string.Equals(turns[last].Id, excludedTurnId, StringComparison.Ordinal))
        {
            count--;
            last--;
        }

        if (last >= 0)
            return new CoveredTurnSet(count, turns[last].Id);
        return count == 0 ? new CoveredTurnSet(0, null) : null;
    }
}
