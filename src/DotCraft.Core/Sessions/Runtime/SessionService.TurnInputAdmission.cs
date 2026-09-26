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
                ClientUserMessageId = queued.ClientUserMessageId,
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

            var message = new ChatMessage(ChatRole.User, contentParts);
            turnModelHistory.Stage(message, item.Id, [queued.Id], async () =>
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

    private static string BuildSubAgentMailboxModelText(IReadOnlyList<SubAgentMailboxEntry> entries)
    {
        var messages = entries
            .Select(entry => entry.ToCommunication().RenderForModel().Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        if (messages.Length == 0)
            return string.Empty;
        return string.Join(Environment.NewLine + Environment.NewLine, messages);
    }

    private static string BuildSubAgentMailboxDisplayText(IReadOnlyList<SubAgentMailboxEntry> entries)
    {
        if (entries.Count == 1)
            return $"SubAgent message from {entries[0].SenderAgentPath}";

        return $"{entries.Count} SubAgent messages";
    }

    private static string StripSystemReminderBlocks(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        const string startTag = "<system-reminder>";
        const string endTag = "</system-reminder>";

        var result = text;
        var searchStart = 0;
        while (searchStart < result.Length)
        {
            var start = result.IndexOf(startTag, searchStart, StringComparison.Ordinal);
            if (start < 0)
                break;

            var end = result.IndexOf(endTag, start + startTag.Length, StringComparison.Ordinal);
            var removeLength = end < 0
                ? result.Length - start
                : end + endTag.Length - start;
            result = result.Remove(start, removeLength);
            searchStart = start;
        }

        return result;
    }
}
