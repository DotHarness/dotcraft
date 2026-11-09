using DotCraft.Agents;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private void SetFailedTurn(
        SessionThread thread,
        SessionTurn turn,
        string errorMsg,
        ProviderFailure? providerFailure = null)
    {
        turn.Status = TurnStatus.Failed;
        turn.Error = errorMsg;
        turn.ProviderError = providerFailure is null
            ? null
            : ProviderFailureKinds.ToWireName(providerFailure.Kind);
        turn.HttpStatus = providerFailure?.HttpStatus;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        RecordFailedTurnReadState(thread, turn);
    }

    private void PublishTurnPersistenceFailure(
        SessionThread thread,
        SessionTurn turn,
        TurnExecutionState? turnRuntime,
        SessionEventChannel channel,
        int itemSequence,
        RolloutPersistenceException error)
    {
        logger?.LogError(error, "Turn persistence failed for thread {ThreadId}", thread.Id);
        var item = CreateErrorItem(turn, itemSequence, error.Message, "persistence_error", fatal: true);
        turn.Items.Add(item);
        channel.EmitItemStarted(item);
        channel.EmitItemCompleted(item);
        FlushTurnDiff(turnRuntime, channel);
        SetFailedTurn(thread, turn, error.Message);
        channel.EmitTurnFailed(turn, error.Message);
        ThreadRuntimeSignalForBroadcast?.Invoke(thread.Id, SessionThreadRuntimeSignal.TurnFailed, turn);
    }

    private async Task<bool> RunCompletedTurnFollowupAsync(SessionThread thread, SessionTurn turn)
    {
        var resourcesReleased = false;
        try
        {
            ThreadRuntimeSignalForBroadcast?.Invoke(
                thread.Id,
                ThreadSummaryRuntime.ContainsSuccessfulCreatePlanInPlanMode(thread, turn)
                    ? SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                    : SessionThreadRuntimeSignal.TurnCompleted,
                turn);
            await ReleaseRetiredThreadToolResourcesIfIdleAsync(thread, CancellationToken.None).ConfigureAwait(false);
            resourcesReleased = true;
            await WakeForAgentMessagesAsync(thread.Id, CancellationToken.None).ConfigureAwait(false);
            await TryStartNextQueuedTurnAsync(thread.Id, CancellationToken.None).ConfigureAwait(false);
            await MaybeContinueGoalIfIdleAsync(thread.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Post-terminal work failed for thread {ThreadId}, turn {TurnId}.", thread.Id, turn.Id);
        }
        return resourcesReleased;
    }
}
