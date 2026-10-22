using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ResidentModelHistory(
    long Revision,
    IReadOnlyList<string> TurnIds,
    IReadOnlyList<ModelHistoryMessage> Messages);

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

        var turnIds = CoveredTurnIds(thread, excludedTurnId);
        var runtime = TryGetCurrentRuntime(thread);
        if (runtime?.ResidentModelHistory is { } resident
            && resident.Revision == persistence.GetModelHistoryRevision(thread.Id).Value
            && resident.TurnIds.SequenceEqual(turnIds, StringComparer.Ordinal))
        {
            var history = ThreadStore.DecodeModelHistory(resident.Messages);
            return (history, new ResidentHistoryBaseline(resident.Revision, resident.Messages, [.. history]));
        }

        var load = await persistence.LoadResidentModelHistoryAsync(thread, excludedTurnId, ct);
        if (!load.Residable)
            return (load.Messages, null);

        var encoded = ThreadStore.EncodeModelHistory(load.Messages);
        if (runtime != null)
            runtime.ResidentModelHistory = new ResidentModelHistory(load.Revision.Value, turnIds, encoded);
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

        runtime.ResidentModelHistory = new ResidentModelHistory(
            revision.Value,
            CoveredTurnIds(thread, excludedTurnId: null),
            messages);
    }

    private void InstallResidentModelHistoryReplacement(
        SessionThread thread,
        string coveredTurnId,
        IReadOnlyList<ChatMessage> replacement)
    {
        if (thread.Ephemeral || TryGetCurrentRuntime(thread) is not { } runtime)
            return;

        var turnIds = CoveredTurnIds(thread, excludedTurnId: null);
        var revision = persistence.GetModelHistoryRevision(thread.Id);
        runtime.ResidentModelHistory = turnIds.Length > 0
            && string.Equals(turnIds[^1], coveredTurnId, StringComparison.Ordinal)
            && revision.LastCheckpoint == revision.Value
                ? new ResidentModelHistory(revision.Value, turnIds, ThreadStore.EncodeModelHistory(replacement))
                : null;
    }

    private ThreadRuntime? TryGetCurrentRuntime(SessionThread thread) =>
        _runtimeRegistry.TryGetRuntime(thread.Id, out var runtime) && ReferenceEquals(runtime.Thread, thread)
            ? runtime
            : null;

    private static string[] CoveredTurnIds(SessionThread thread, string? excludedTurnId) =>
        thread.Turns
            .Where(turn => !string.Equals(turn.Id, excludedTurnId, StringComparison.Ordinal))
            .Select(static turn => turn.Id)
            .ToArray();
}
