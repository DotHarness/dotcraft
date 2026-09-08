using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task ReconcilePersistedInputHistoryAsync(
        SessionThread thread, IReadOnlyList<ChatMessage> history, CancellationToken ct)
    {
        var inputs = history.SelectMany(message => TurnModelHistory.Inputs(message)
                .Select(identity => (Identity: identity, Message: message)))
            .DistinctBy(input => input.Identity.InputId)
            .ToDictionary(input => input.Identity.InputId);
        if (inputs.Count == 0) return;

        using (await AcquireThreadQueueLockAsync(thread.Id, ct))
        {
            var consumed = thread.QueuedInputs.Where(input => inputs.ContainsKey(input.Id)).ToArray();
            foreach (var input in consumed)
            {
                var incorporated = inputs[input.Id];
                var turn = thread.Turns.Single(turn => turn.Id == incorporated.Identity.TurnId);
                var itemId = incorporated.Identity.ItemId;
                if (!turn.Items.Any(item => item.Id == itemId))
                {
                    var images = ExtractUserMessageImages(incorporated.Message.Contents);
                    turn.Items.Add(new SessionItem
                    {
                        Id = itemId, TurnId = turn.Id, Type = ItemType.UserMessage, Status = ItemStatus.Completed,
                        CreatedAt = input.CreatedAt, CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new UserMessagePayload
                        {
                            Text = input.DisplayText ?? SessionWireMapper.BuildDisplayText(input.NativeInputParts),
                            DeliveryMode = "guidance", NativeInputParts = input.NativeInputParts,
                            MaterializedInputParts = input.MaterializedInputParts,
                            SenderId = input.Sender?.SenderId, SenderName = input.Sender?.SenderName,
                            SenderRole = input.Sender?.SenderRole, ChannelName = turn.OriginChannel,
                            ChannelContext = turn.Initiator?.ChannelContext,
                            GroupId = input.Sender?.GroupId ?? turn.Initiator?.GroupId,
                            TriggerKind = input.TriggerKind, TriggerLabel = input.TriggerLabel,
                            TriggerRefId = input.TriggerRefId, QueuedInputId = input.Id,
                            DeliveryBindingId = input.DeliveryBindingId,
                            Images = images.Count > 0 ? images : null
                        }
                    });
                }
                thread.QueuedInputs = thread.QueuedInputs.Where(queued => queued.Id != input.Id).ToList();
            }
            if (consumed.Length > 0)
                await persistence.SaveThreadAsync(thread, ct);
        }

        var root = thread.Source.SubAgent?.RootThreadId ?? thread.Id;
        var path = thread.Source.SubAgent?.AgentPath ?? AgentPath.Root;
        using (await _subAgentCommunicationRuntime.AcquireInboxAsync(root, path, ct))
        {
            var pending = await ListPendingSubAgentMailboxAsync(root, path, ct);
            var delivered = pending.Where(entry => inputs.ContainsKey($"mailbox:{entry.Id}")).ToArray();
            if (delivered.Length > 0)
            {
                foreach (var batch in delivered.GroupBy(entry => inputs[$"mailbox:{entry.Id}"].Identity.ItemId))
                {
                    var turnId = inputs[$"mailbox:{batch.First().Id}"].Identity.TurnId;
                    var turn = thread.Turns.Single(turn => turn.Id == turnId);
                    if (turn.Items.Any(item => item.Id == batch.Key)) continue;
                    var entries = batch.ToArray();
                    var text = BuildSubAgentMailboxDisplayText(entries);
                    turn.Items.Add(new SessionItem
                    {
                        Id = batch.Key, TurnId = turn.Id, Type = ItemType.UserMessage, Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new UserMessagePayload
                        {
                            Text = text, DeliveryMode = SubAgentMailboxDelivery.DeliveryMode,
                            NativeInputParts = [new() { Type = "text", Text = text }],
                            MaterializedInputParts = [new() { Type = "text", Text = BuildSubAgentMailboxModelText(entries) }],
                            ChannelName = turn.OriginChannel, ChannelContext = turn.Initiator?.ChannelContext,
                            GroupId = turn.Initiator?.GroupId, TriggerKind = SubAgentMailboxDelivery.DeliveryMode,
                            TriggerLabel = entries.Length == 1 ? entries[0].SenderAgentPath : $"{entries.Length} messages",
                            TriggerRefId = path
                        }
                    });
                }
                await persistence.SaveThreadAsync(thread, ct);
                await MarkSubAgentMailboxDeliveredAsync(root, delivered.Select(entry => entry.Id).ToArray(),
                    DateTimeOffset.UtcNow, ct);
            }
        }
    }
}
