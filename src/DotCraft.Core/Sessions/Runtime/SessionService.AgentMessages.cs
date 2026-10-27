namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    internal async Task WakeForAgentMessagesAsync(string threadId, CancellationToken ct)
    {
        var wake = await InvokeThreadCommandAsync(
            threadId,
            commandCt => EnqueuePendingAgentMessagesAsync(threadId, commandCt),
            ct).ConfigureAwait(false);
        if (wake)
            await TryStartNextQueuedTurnAsync(threadId, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<bool> EnqueuePendingAgentMessagesAsync(string threadId, CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct).ConfigureAwait(false);
        if (thread.Status != ThreadStatus.Active || !SubAgentSessionControl.IsNativeRuntime(thread))
            return false;

        var source = thread.Source.SubAgent;
        var rootThreadId = string.IsNullOrWhiteSpace(source?.RootThreadId) ? thread.Id : source.RootThreadId;
        var agentPath = string.IsNullOrWhiteSpace(source?.AgentPath) ? AgentPath.Root : source.AgentPath;
        using var inbox = await _subAgentCommunicationRuntime.AcquireInboxAsync(rootThreadId, agentPath, ct).ConfigureAwait(false);
        if (thread.Turns.Any(static turn => turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
            return false;

        var pending = await ListPendingSubAgentMailboxAsync(rootThreadId, agentPath, ct).ConfigureAwait(false);
        if (pending.Count == 0)
            return false;

        var displayText = BuildSubAgentMailboxDisplayText(pending);
        var queued = new QueuedTurnInput
        {
            Id = SessionIdGenerator.NewQueuedInputId(),
            ThreadId = thread.Id,
            NativeInputParts = [new SessionInputPart { Type = "text", Text = displayText }],
            MaterializedInputParts = [new SessionInputPart { Type = "text", Text = BuildSubAgentMailboxModelText(pending) }],
            DisplayText = displayText,
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow,
            TriggerKind = SubAgentMailboxDelivery.DeliveryMode,
            TriggerRefId = agentPath
        };
        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        using (await AcquireThreadQueueLockAsync(threadId, ct).ConfigureAwait(false))
        {
            if (_runtimeRegistry.TryGetThread(threadId, out var cachedThread))
                thread = cachedThread;
            var queue = thread.QueuedInputs.ToList();
            queue.Add(queued);
            thread.QueuedInputs = queue;
            await PersistThreadChangesAsync(thread, [RolloutChangeRecords.QueuedInputAdded(thread.Id, queued)], ct).ConfigureAwait(false);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        await MarkSubAgentMailboxDeliveredAsync(
            rootThreadId,
            pending.Select(static entry => entry.Id).ToArray(),
            DateTimeOffset.UtcNow,
            CancellationToken.None).ConfigureAwait(false);
        return true;
    }
}
