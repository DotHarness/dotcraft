using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private readonly ConcurrentDictionary<string, object> _readStateWriteLocks = new(StringComparer.Ordinal);

    public Action<string, ThreadReadState>? ThreadReadStateChangedForBroadcast { get; set; }

    public Task<ThreadReadState?> GetThreadReadStateAsync(SessionThread thread, CancellationToken ct = default) =>
        Task.FromResult(ThreadReadReceipts.IsEligible(thread) ? Persistence.ReadStates.Load(thread.Id) : null);

    public Task<IReadOnlyDictionary<string, ThreadReadState>?> GetThreadReadStatesAsync(
        IReadOnlyList<ThreadSummary> threads,
        CancellationToken ct = default)
    {
        var eligible = threads
            .Where(ThreadReadReceipts.IsEligible)
            .Select(static summary => summary.Id)
            .ToArray();
        return Task.FromResult<IReadOnlyDictionary<string, ThreadReadState>?>(Persistence.ReadStates.LoadMany(eligible));
    }

    public async Task<ThreadReadState> UpdateThreadReadStateAsync(
        string threadId,
        string expectedRevision,
        ThreadReadStateOperation operation,
        CancellationToken ct = default)
    {
        var normalized = NormalizeRequiredThreadId(threadId);
        var thread = TryGetLoadedThread(normalized)
                     ?? (Persistence.ThreadExists(normalized)
                         ? (await Persistence.ReadThreadSnapshotAsync(normalized, ct)).Thread
                         : throw new ThreadNotFoundException(normalized));
        if (!ThreadReadReceipts.IsEligible(thread))
            throw new ThreadReadStateUnavailableException(normalized);

        ThreadReadStateUpdateOutcome? outcome = null;
        WriteReadState(normalized, () =>
        {
            outcome = Persistence.ReadStates.Update(normalized, expectedRevision, operation);
            return outcome.Applied ? outcome.State : null;
        });
        if (!outcome!.Applied)
            throw new ThreadReadStateConflictException(normalized, outcome.State);
        return outcome.State;
    }

    private async Task RecordTurnResultReadStateAsync(SessionThread thread, SessionTurn turn)
    {
        if (!IsNewResult(thread, turn))
            return;

        try
        {
            if (Goals.AutoContinues
                && await Persistence.GetThreadGoalAsync(thread.Id) is { Status: ThreadGoalStatus.Active })
                return;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to read the goal of thread {ThreadId} for its read receipt.", thread.Id);
        }

        ApplyTurnResultReadState(thread, turn);
    }

    private void RecordFailedTurnReadState(SessionThread thread, SessionTurn turn)
    {
        if (IsNewResult(thread, turn))
            ApplyTurnResultReadState(thread, turn);
    }

    private void ApplyTurnResultReadState(SessionThread thread, SessionTurn turn)
    {
        try
        {
            WriteReadState(thread.Id, () => Persistence.ReadStates.ApplyNewResult(thread.Id, turn.Id));
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to record the read receipt of thread {ThreadId} for turn {TurnId}.", thread.Id, turn.Id);
        }
    }

    private async Task ResetReadStateAfterRollbackAsync(SessionThread thread)
    {
        if (!ThreadReadReceipts.IsEligible(thread))
            return;

        try
        {
            var firstUnreadTurnId = Persistence.ReadStates.Load(thread.Id).FirstUnread?.TurnId;
            var removed = firstUnreadTurnId is not null
                          && thread.Turns.All(turn => !string.Equals(turn.Id, firstUnreadTurnId, StringComparison.Ordinal))
                          && (thread.OmittedTurnCount == 0
                              || (await Persistence.ReadProjectedTurnsAsync(thread.Id, [firstUnreadTurnId])).Count == 0);
            WriteReadState(thread.Id, () => Persistence.ReadStates.ResetAfterRollback(thread.Id, removed ? firstUnreadTurnId : null));
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to update the read receipt of thread {ThreadId} after rollback.", thread.Id);
        }
    }

    private ThreadReadState? WriteReadState(string threadId, Func<ThreadReadState?> write)
    {
        lock (_readStateWriteLocks.GetOrAdd(threadId, static _ => new object()))
        {
            var state = write();
            if (state is not null)
                ThreadReadStateChangedForBroadcast?.Invoke(threadId, state);
            return state;
        }
    }

    private static bool IsNewResult(SessionThread thread, SessionTurn turn) =>
        ThreadReadReceipts.IsEligible(thread) && DeliversNewResult(turn);

    private static bool DeliversNewResult(SessionTurn turn) => turn.Status switch
    {
        TurnStatus.Completed => HasError(turn)
                                || HasText(turn.Items.LastOrDefault(static item => item.Type == ItemType.AgentMessage)),
        TurnStatus.Failed => true,
        TurnStatus.Cancelled => HasError(turn)
                                || turn.Items.Any(static item => item.Type == ItemType.AgentMessage && HasText(item)),
        _ => false
    };

    private static bool HasError(SessionTurn turn) =>
        !string.IsNullOrWhiteSpace(turn.Error) || turn.Items.Any(static item => item.Type == ItemType.Error);

    private static bool HasText(SessionItem? item) =>
        !string.IsNullOrWhiteSpace(item?.AsAgentMessage?.Text);
}
