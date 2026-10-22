using DotCraft.Context.WorldState;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal Task<ProviderHistorySnapshot> LoadProviderHistoryAsync(
        SessionThread thread,
        string currentContextWindowId,
        CancellationToken ct = default) =>
        LoadProviderHistoryAsync(thread, currentContextWindowId, retain: false, ct);

    internal Task<ProviderHistorySnapshot> LoadResidentProviderHistoryAsync(
        SessionThread thread,
        string currentContextWindowId,
        CancellationToken ct = default) =>
        LoadProviderHistoryAsync(thread, currentContextWindowId, retain: true, ct);

    private async Task<ProviderHistorySnapshot> LoadProviderHistoryAsync(
        SessionThread thread,
        string currentContextWindowId,
        bool retain,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(thread);
        if (thread.ProviderHistorySchemaVersion != ProviderHistorySchema.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"responses_provider_history_corrupt: Thread '{thread.Id}' uses unsupported provider history schema version {thread.ProviderHistorySchemaVersion}.");
        }

        var (records, latestCheckpointAt) = await ReadProviderHistoryInputsAsync(thread.Id, retain, ct);
        var latestReplacementTimestamp = records
            .Where(static record => record.ProviderHistoryReplaced is not null)
            .Select(static record => (DateTimeOffset?)record.Timestamp)
            .LastOrDefault();
        if (latestCheckpointAt is not null
            && (!latestReplacementTimestamp.HasValue
                || latestCheckpointAt.Value > latestReplacementTimestamp.Value))
        {
            // The rollout checkpoint is authoritative. A process may have stopped after committing
            // it but before publishing the derived provider-history replacement. Returning an empty
            // snapshot makes Session Core advance the window and rebuild the provider prefix from
            // the replayed compacted history.
            return ProviderHistorySnapshot.Empty(currentContextWindowId);
        }

        var survivingTurnIds = await ReadVisibleTurnIdsAsync(
            thread,
            records
                .SelectMany(static record => new[]
                {
                    record.ProviderHistoryReplaced?.CoveredThroughTurnId,
                    record.ProviderHistoryItemsAppended?.TurnId,
                    record.ProviderHistoryAttemptAborted?.TurnId
                })
                .OfType<string>()
                .ToList(),
            ct);
        return ProviderHistoryReplayer.Replay(
            thread.Id,
            currentContextWindowId,
            survivingTurnIds,
            records);
    }

    /// <summary>
    /// Rebuilds MEAI model history from canonical rollout records.
    /// </summary>
    internal async Task<List<ChatMessage>> LoadModelHistoryAsync(
        SessionThread thread,
        string? excludedTurnId = null,
        CancellationToken ct = default) =>
        (await RebuildModelHistoryFromRolloutAsync(thread, excludedTurnId, ct)).History;

    internal async Task<WorldStateSnapshot?> LoadWorldStateBaselineAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var rolloutPath = _rolloutStore.ResolveExistingPath(thread.Id);
        if (rolloutPath == null)
            return null;

        Interlocked.Increment(ref _rolloutReplayCount);
        var replayed = await _rolloutReplayer.ReplayModelHistoryAsync(
            rolloutPath,
            await ReadVisibleTurnIdsAsync(thread, ct),
            (ids, token) => ReadVisibleTurnsAsync(thread, ids, token),
            excludedTurnId: null,
            ct,
            thread.Id);
        return replayed.WorldState;
    }

    private async Task<(List<ChatMessage> History, bool Projected)> RebuildModelHistoryFromRolloutAsync(
        SessionThread thread,
        string? excludedTurnId,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _rolloutReplayCount);
        var rolloutPath = _rolloutStore.ResolveExistingPath(thread.Id);
        IReadOnlyList<string> turnIds = rolloutPath == null
            ? thread.Turns.Select(static turn => turn.Id).ToList()
            : await ReadVisibleTurnIdsAsync(thread, ct);
        var replayed = rolloutPath == null
            ? new ModelHistoryReplayResult([], HasModelHistoryRecords: false)
            : await _rolloutReplayer.ReplayModelHistoryAsync(
                rolloutPath,
                turnIds,
                (ids, token) => ReadVisibleTurnsAsync(thread, ids, token),
                excludedTurnId,
                ct,
                thread.Id);
        foreach (var warning in replayed.Warnings ?? [])
        {
            System.Diagnostics.Trace.TraceWarning(
                string.IsNullOrWhiteSpace(warning.TurnId)
                    ? $"Rollout model-history replay warning ({warning.Code}): {warning.Message}"
                    : $"Rollout model-history replay warning for turn '{warning.TurnId}' ({warning.Code}): {warning.Message}");
        }
        IReadOnlyList<SessionTurn> fallbackTurns = replayed.HasModelHistoryRecords
            ? []
            : await ReadVisibleTurnsAsync(
                thread,
                turnIds.Where(id => !string.Equals(id, excludedTurnId, StringComparison.Ordinal)).ToList(),
                ct);
        var history = replayed.HasModelHistoryRecords
            ? replayed.Messages.ToList()
            : BuildModelVisibleHistoryFromTurns(fallbackTurns);
        history = MessageGrouper.NormalizeFunctionCallArguments(history).ToList();
        var projected = replayed.HasModelHistoryRecords
            ? replayed.FallbackTurnIds is { Count: > 0 }
            : fallbackTurns.Count > 0;

        return (history, projected);
    }
}
