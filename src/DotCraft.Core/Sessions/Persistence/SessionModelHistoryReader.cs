using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

/// <summary>
/// Replays the canonical model-visible history from a persisted thread rollout.
/// </summary>
public sealed class SessionModelHistoryReader
{
    /// <summary>
    /// Replays model-visible history for the surviving turns in a rollout.
    /// </summary>
    /// <param name="rolloutPath">Path to the persisted rollout.</param>
    /// <param name="survivingTurns">The ordered turns that survive rollback.</param>
    /// <param name="excludedTurnId">An optional current turn to exclude.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="expectedThreadId">The thread identity expected in persisted records.</param>
    /// <returns>The reconstructed model-visible history and replay diagnostics.</returns>
    public async Task<SessionModelHistoryReplay> ReplayAsync(
        string rolloutPath,
        IReadOnlyList<SessionTurn> survivingTurns,
        string? excludedTurnId = null,
        CancellationToken ct = default,
        string? expectedThreadId = null)
    {
        var replay = await new RolloutReplayer().ReplayModelHistoryAsync(
            rolloutPath,
            survivingTurns,
            excludedTurnId,
            ct,
            expectedThreadId).ConfigureAwait(false);

        return new SessionModelHistoryReplay(
            replay.Messages,
            replay.HasModelHistoryRecords,
            replay.Warnings?.Select(static warning => new SessionModelHistoryWarning(
                warning.Code,
                warning.Message,
                warning.TurnId)).ToArray(),
            replay.RejectedRecords,
            replay.FallbackTurnIds,
            replay.BytesRead,
            replay.RecordsDecoded);
    }
}

/// <summary>
/// Result of replaying canonical model-visible history from a thread rollout.
/// </summary>
/// <param name="Messages">The reconstructed messages in model-visible order.</param>
/// <param name="HasModelHistoryRecords">Whether exact model-history records contributed to replay.</param>
/// <param name="Warnings">Non-fatal replay warnings.</param>
/// <param name="RejectedRecords">Number of persisted records rejected during replay.</param>
/// <param name="FallbackTurnIds">Turns reconstructed from visible items.</param>
/// <param name="BytesRead">Number of rollout bytes read.</param>
/// <param name="RecordsDecoded">Number of rollout records decoded.</param>
public sealed record SessionModelHistoryReplay(
    IReadOnlyList<ChatMessage> Messages,
    bool HasModelHistoryRecords,
    IReadOnlyList<SessionModelHistoryWarning>? Warnings = null,
    int RejectedRecords = 0,
    IReadOnlySet<string>? FallbackTurnIds = null,
    long BytesRead = 0,
    int RecordsDecoded = 0);

/// <summary>
/// A non-fatal warning produced while replaying model-visible history.
/// </summary>
/// <param name="Code">Stable warning code.</param>
/// <param name="Message">English diagnostic message.</param>
/// <param name="TurnId">Associated turn identity, when available.</param>
public sealed record SessionModelHistoryWarning(
    string Code,
    string Message,
    string? TurnId = null);
