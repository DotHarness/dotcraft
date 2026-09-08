using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task<ChatMessage?> AdmitGuidanceInputAsync(
        SessionThread thread, SessionTurn turn, SessionEventChannel eventChannel,
        Func<int> nextItemSeq, Action finalizeStreamingAgentMessage, Action finalizeStreamingReasoning,
        TurnModelHistory turnModelHistory, CancellationToken drainCt)
    {
        QueuedTurnInput? queued;
        IReadOnlyList<QueuedTurnInput>? cleanupSnapshot = null;
        using (await AcquireThreadQueueLockAsync(thread.Id, drainCt))
        {
            var queue = thread.QueuedInputs.ToList();
            if (queue.RemoveAll(IsLegacyGoalBudgetGuidanceInput) > 0)
            {
                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, drainCt);
                cleanupSnapshot = queue.ToList();
            }

            var queueIndex = queue.FindIndex(q =>
                string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
            queued = queueIndex < 0 ? null : queue[queueIndex];
        }
        if (cleanupSnapshot != null)
            PublishQueueUpdated(thread.Id, cleanupSnapshot);
        if (queued == null)
            return null;

        var contentParts = await ThreadQueue.ResolveInputPartsAsync(queued.MaterializedInputParts.ToList(), drainCt);
        if (contentParts.Count == 0)
            return null;

        var nativeParts = queued.NativeInputParts.ToList();
        var materializedParts = queued.MaterializedInputParts.ToList();
        var displayText = !string.IsNullOrWhiteSpace(queued.DisplayText)
            ? queued.DisplayText
            : SessionWireMapper.BuildDisplayText(nativeParts);
        var images = ExtractUserMessageImages(contentParts);

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = displayText,
                DeliveryMode = "guidance",
                NativeInputParts = nativeParts,
                MaterializedInputParts = materializedParts,
                SenderId = queued.Sender?.SenderId,
                SenderName = queued.Sender?.SenderName,
                SenderRole = queued.Sender?.SenderRole,
                ChannelName = turn.OriginChannel,
                ChannelContext = turn.Initiator?.ChannelContext,
                GroupId = queued.Sender?.GroupId ?? turn.Initiator?.GroupId,
                Images = images.Count > 0 ? images : null,
                TriggerKind = queued.TriggerKind,
                TriggerLabel = queued.TriggerLabel,
                TriggerRefId = queued.TriggerRefId,
                QueuedInputId = queued.Id,
                DeliveryBindingId = queued.DeliveryBindingId
            }
        };

        var queueLease = await AcquireThreadQueueLockAsync(thread.Id, CancellationToken.None);
        var staged = false;
        try
        {
            var queue = thread.QueuedInputs.ToList();
            var queueIndex = queue.FindIndex(q =>
                string.Equals(q.Id, queued.Id, StringComparison.Ordinal) &&
                string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
            if (queueIndex < 0)
                return null;

            finalizeStreamingAgentMessage();
            finalizeStreamingReasoning();

            var message = new ChatMessage(ChatRole.User, contentParts) { MessageId = item.Id };
            turnModelHistory.Stage(message, [queued.Id], async () =>
            {
                turn.Items.Add(item);
                queue.RemoveAt(queueIndex);
                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                eventChannel.EmitItemStarted(item);
                eventChannel.EmitItemCompleted(item);
                PublishQueueUpdated(thread.Id, queue.ToList());
            }, queueLease);
            staged = true;
            return message;
        }
        finally { if (!staged) queueLease.Dispose(); }
    }
}
