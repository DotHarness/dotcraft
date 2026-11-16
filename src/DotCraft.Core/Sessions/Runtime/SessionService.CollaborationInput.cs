using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task ReconcileBoardInputHistoryAsync(
        SessionThread thread,
        IReadOnlyList<ChatMessage> history,
        RecoveredInputItems recoveredItems,
        CancellationToken ct)
    {
        var recovered = false;
        foreach (var message in history)
        {
            var identity = TurnModelHistory.Inputs(message).FirstOrDefault(input => input.InputId.StartsWith("mailbox:board:", StringComparison.Ordinal));
            if (identity == null) continue;
            var turn = await recoveredItems.FindTurnAsync(identity.TurnId, ct);
            if (turn == null || turn.Items.Any(item => item.Id == identity.ItemId)) continue;
            const string displayText = "Agent message board notification";
            recoveredItems.Add(turn, new SessionItem
            {
                Id = identity.ItemId, TurnId = turn.Id, Type = ItemType.UserMessage, Status = ItemStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
                Payload = new UserMessagePayload
                {
                    Text = displayText, DeliveryMode = SubAgentMailboxDelivery.DeliveryMode,
                    NativeInputParts = [new() { Type = "text", Text = displayText }],
                    MaterializedInputParts = [new() { Type = "text", Text = message.Text }],
                    ChannelName = turn.OriginChannel, ChannelContext = turn.Initiator?.ChannelContext,
                    GroupId = turn.Initiator?.GroupId, TriggerKind = SubAgentMailboxDelivery.DeliveryMode
                }
            });
            recovered = true;
        }
        if (recovered) await recoveredItems.PersistAsync(ct);
    }

    private async Task<ChatMessage?> DrainCollaborationMailboxAsync(
        (IDisposable Lease, string RootThreadId, string AgentPath) inbox,
        SessionThread thread, SessionTurn turn, SessionEventChannel events, Func<int> nextItem,
        Action finalizeMessage, Action finalizeReasoning, TurnModelHistory history, CancellationToken ct)
    {
        var staged = false;
        try
        {
            var pending = await ListPendingSubAgentMailboxAsync(inbox.RootThreadId, inbox.AgentPath, ct);
            if (pending.Count == 0) return null;
            var sender = await ResolveMailboxSenderAsync(pending, ct);
            var message = StageCollaborationInput(pending, sender, thread, turn, events, nextItem, finalizeMessage,
                finalizeReasoning, history, inbox.Lease, () => MarkSubAgentMailboxDeliveredAsync(inbox.RootThreadId,
                    pending.Select(entry => entry.Id).ToArray(), DateTimeOffset.UtcNow, CancellationToken.None));
            staged = message != null;
            return message;
        }
        finally { if (!staged) inbox.Lease.Dispose(); }
    }

    private async Task<ChatMessage?> DrainBoardInputAsync(BoardTurnInbox inbox, SessionThread thread, SessionTurn turn,
        SessionEventChannel events, Func<int> nextItem, Action finalizeMessage, Action finalizeReasoning,
        TurnModelHistory history, CancellationToken ct)
    {
        var pending = inbox.Drain();
        if (pending.Length == 0) return null;
        var sender = await ResolveMailboxSenderAsync(pending, ct);
        return StageCollaborationInput(pending, sender, thread, turn, events, nextItem,
            finalizeMessage, finalizeReasoning, history, null, () => Task.CompletedTask);
    }

    private ChatMessage? StageCollaborationInput(IReadOnlyList<SubAgentMailboxEntry> pending,
        (string? Label, string? RefId) sender, SessionThread thread, SessionTurn turn, SessionEventChannel events, Func<int> nextItem,
        Action finalizeMessage, Action finalizeReasoning, TurnModelHistory history, IDisposable? lease, Func<Task> delivered)
    {
        var materializedText = BuildSubAgentMailboxModelText(pending);
        if (string.IsNullOrWhiteSpace(materializedText)) return null;
        var displayText = BuildSubAgentMailboxDisplayText(pending);
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItem()), TurnId = turn.Id, Type = ItemType.UserMessage,
            Status = ItemStatus.Completed, CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = displayText, DeliveryMode = SubAgentMailboxDelivery.DeliveryMode,
                NativeInputParts = [new SessionInputPart { Type = "text", Text = displayText }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = materializedText }],
                ChannelName = turn.OriginChannel, ChannelContext = turn.Initiator?.ChannelContext, GroupId = turn.Initiator?.GroupId,
                TriggerKind = SubAgentMailboxDelivery.DeliveryMode,
                TriggerLabel = sender.Label,
                TriggerRefId = sender.RefId
            }
        };
        finalizeMessage();
        finalizeReasoning();
        var message = new ChatMessage(ChatRole.User, (IList<AIContent>)[new TextContent(materializedText)]);
        history.Stage([new TurnModelHistory.StagedInput(message, item.Id, pending.Select(entry => $"mailbox:{entry.Id}").ToArray())], async () =>
        {
            turn.Items.Add(item);
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
            await delivered();
            events.EmitItemStarted(item);
            events.EmitItemCompleted(item);
        }, lease);
        return message;
    }

}
