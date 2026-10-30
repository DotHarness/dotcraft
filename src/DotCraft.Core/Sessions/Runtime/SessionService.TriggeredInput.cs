namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    /// <summary>An active Turn takes the input as guidance; otherwise it stays queued and the caller decides whether to wake the thread.</summary>
    private async Task<(QueuedTurnInput Queued, SessionThread Thread, bool SteeredIntoActiveTurn)> EnqueueTriggeredInputAsync(
        string threadId,
        QueuedTurnInput input,
        CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        QueuedTurnInput queued;
        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        SessionTurn? activeTurn;
        using (await AcquireThreadQueueLockAsync(threadId, ct).ConfigureAwait(false))
        {
            if (_runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                thread = cachedThread;

            activeTurn = thread.Status == ThreadStatus.Active
                ? thread.Turns.LastOrDefault(static turn =>
                    turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                : null;
            queued = input with
            {
                ThreadId = thread.Id,
                Status = activeTurn == null ? "queued" : "guidancePending",
                ReadyAfterTurnId = activeTurn?.Id
            };
            var queue = thread.QueuedInputs.ToList();
            queue.Add(queued);
            thread.QueuedInputs = queue;
            await PersistThreadChangesAsync(thread, [RolloutChangeRecords.QueuedInputAdded(thread.Id, queued)], ct).ConfigureAwait(false);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        if (activeTurn == null)
            return (queued, thread, false);

        var source = thread.Source.SubAgent;
        _subAgentCommunicationRuntime.PublishSteer(
            string.IsNullOrWhiteSpace(source?.RootThreadId) ? thread.Id : source.RootThreadId,
            string.IsNullOrWhiteSpace(source?.AgentPath) ? AgentPath.Root : source.AgentPath);
        return (queued, thread, true);
    }
}
