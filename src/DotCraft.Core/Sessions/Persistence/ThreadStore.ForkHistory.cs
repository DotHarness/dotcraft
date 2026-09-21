using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal sealed record ForkModelHistoryMaterialization(
    IReadOnlyList<ChatMessage> History,
    IReadOnlyList<ChatMessage>? CheckpointReplacementHistory,
    string? CheckpointCoveredThroughTurnId,
    string? CheckpointMode,
    long EstimatedTokens,
    string UsageSource,
    bool UsageIsEstimate,
    IReadOnlyDictionary<string, IReadOnlyList<ChatMessage>> TurnHistories)
{
    public bool HasCompatibleCheckpoint =>
        CheckpointReplacementHistory is not null &&
        !string.IsNullOrWhiteSpace(CheckpointCoveredThroughTurnId);
}

public sealed partial class ThreadStore
{
    internal async Task<ForkModelHistoryMaterialization> BuildForkModelHistoryMaterializationAsync(
        SessionThread source,
        SessionThread forked,
        CancellationToken ct = default,
        IReadOnlyDictionary<string, ChatMessage>? interruptions = null)
    {
        ct.ThrowIfCancellationRequested();
        var orderedForkTurns = OrderTurns(forked.Turns);
        var turnHistories = orderedForkTurns.ToDictionary(turn => turn.Id, turn =>
        {
            var messages = BuildModelVisibleHistoryFromTurn(turn).ToList();
            if (interruptions?.TryGetValue(turn.Id, out var marker) == true)
                messages.Add(marker.Clone());
            return (IReadOnlyList<ChatMessage>)messages;
        });
        var checkpoints = await _rolloutStore.LoadCompactionCheckpointsAsync(source.Id, ct);
        for (var i = checkpoints.Count - 1; i >= 0; i--)
        {
            var checkpoint = checkpoints[i];
            if (!IsCheckpointCompatibleForFork(source, orderedForkTurns, checkpoint))
                continue;

            if (!TryDeserializeCheckpointHistory(checkpoint, out var checkpointHistory))
                continue;

            var history = checkpointHistory.ToList();
            var coveredTurnIndex = orderedForkTurns.FindIndex(turn =>
                string.Equals(turn.Id, checkpoint.CoveredThroughTurnId, StringComparison.Ordinal));
            for (var turnIndex = coveredTurnIndex + 1; turnIndex < orderedForkTurns.Count; turnIndex++)
                history.AddRange(turnHistories[orderedForkTurns[turnIndex].Id]);

            return new ForkModelHistoryMaterialization(
                history,
                checkpointHistory,
                checkpoint.CoveredThroughTurnId,
                checkpoint.Mode,
                MessageTokenEstimator.Estimate(history),
                "compacted_estimate",
                UsageIsEstimate: true,
                turnHistories);
        }

        var rawHistory = orderedForkTurns.SelectMany(turn => turnHistories[turn.Id]).ToList();
        return new ForkModelHistoryMaterialization(
            rawHistory,
            CheckpointReplacementHistory: null,
            CheckpointCoveredThroughTurnId: null,
            CheckpointMode: null,
            MessageTokenEstimator.Estimate(rawHistory),
            "history_estimate",
            UsageIsEstimate: true,
            turnHistories);
    }
}
