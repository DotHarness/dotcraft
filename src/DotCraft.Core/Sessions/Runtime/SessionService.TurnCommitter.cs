using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>
    /// Owns the single terminal persistence boundary for an admitted Turn. It deliberately
    /// knows nothing about streaming or failure policy; callers prepare the final Turn first.
    /// </summary>
    private sealed class TurnCommitter(
        SessionService owner,
        SessionThread thread,
        SessionTurn turn)
    {
        public List<ChatMessage>? Session { get; set; }

        public int PersistedModelHistoryCount { get; set; }

        public PendingCompactionCheckpoint? PendingCompactionCheckpoint { get; set; }

        public async Task CommitAsync()
        {
            IReadOnlyList<ChatMessage> modelHistory;
            if (Session != null && TrySnapshotInMemoryHistory(Session, out var currentHistory))
            {
                if (PersistedModelHistoryCount < 0 || PersistedModelHistoryCount > currentHistory.Count)
                {
                    throw new InvalidOperationException(
                        $"Invalid model-history prefix length for thread '{thread.Id}'.");
                }

                modelHistory = currentHistory.Skip(PersistedModelHistoryCount).ToList();
            }
            else
            {
                modelHistory = ThreadStore.BuildModelVisibleHistoryFromTurn(turn);
            }

            var checkpoint = PendingCompactionCheckpoint;
            var compaction = checkpoint == null
                ? null
                : new TurnCompactionHistory(
                    checkpoint.Trigger,
                    checkpoint.Mode,
                    checkpoint.TokensBefore,
                    checkpoint.TokensAfter,
                    checkpoint.ReplacementHistory ?? []);
            await owner.PersistTurnCommitWithMaterializationAsync(
                thread,
                turn,
                modelHistory,
                compaction,
                CancellationToken.None);
            PendingCompactionCheckpoint = null;
            PersistedModelHistoryCount += modelHistory.Count;
        }
    }
}
