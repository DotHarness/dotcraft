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

        var recovered = new RecoveredInputItems(this, thread);
        await ReconcileBoardInputHistoryAsync(thread, history, recovered, ct);

        using (await AcquireThreadQueueLockAsync(thread.Id, ct))
        {
            var consumed = thread.QueuedInputs.Where(input => inputs.ContainsKey(input.Id)).ToArray();
            foreach (var input in consumed)
            {
                var incorporated = inputs[input.Id];
                var turn = await recovered.TurnAsync(incorporated.Identity.TurnId, ct);
                var itemId = incorporated.Identity.ItemId;
                if (!turn.Items.Any(item => item.Id == itemId))
                {
                    var images = ExtractUserMessageImages(incorporated.Message.Contents);
                    recovered.Add(turn, new SessionItem
                    {
                        Id = itemId, TurnId = turn.Id, Type = ItemType.UserMessage, Status = ItemStatus.Completed,
                        CreatedAt = input.CreatedAt, CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new UserMessagePayload
                        {
                            Text = input.DisplayText ?? SessionWireMapper.BuildDisplayText(input.NativeInputParts),
                            DeliveryMode = "guidance", ClientUserMessageId = input.ClientUserMessageId, NativeInputParts = input.NativeInputParts,
                            MaterializedInputParts = input.MaterializedInputParts,
                            SenderId = input.Sender?.SenderId, SenderName = input.Sender?.SenderName,
                            SenderRole = input.Sender?.SenderRole, ChannelName = turn.OriginChannel,
                            ChannelContext = turn.Initiator?.ChannelContext,
                            GroupId = input.Sender?.GroupId ?? turn.Initiator?.GroupId,
                            TriggerKind = input.TriggerKind, TriggerLabel = input.TriggerLabel,
                            TriggerRefId = input.TriggerRefId, TriggerStatus = input.TriggerStatus, QueuedInputId = input.Id,
                            Images = images.Count > 0 ? images : null
                        }
                    });
                }
                thread.QueuedInputs = thread.QueuedInputs.Where(queued => queued.Id != input.Id).ToList();
            }
            if (consumed.Length > 0)
                await recovered.PersistAsync(ct);
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
                    var turn = await recovered.TurnAsync(turnId, ct);
                    if (turn.Items.Any(item => item.Id == batch.Key)) continue;
                    var entries = batch.ToArray();
                    var text = BuildSubAgentMailboxDisplayText(entries);
                    recovered.Add(turn, new SessionItem
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
                await recovered.PersistAsync(ct);
                await MarkSubAgentMailboxDeliveredAsync(root, delivered.Select(entry => entry.Id).ToArray(),
                    DateTimeOffset.UtcNow, ct);
            }
        }
    }

    private sealed class RecoveredInputItems(SessionService owner, SessionThread thread)
    {
        private readonly Dictionary<string, SessionTurn> _omitted = new(StringComparer.Ordinal);
        private readonly List<ThreadRolloutRecord> _records = [];

        public async Task<SessionTurn> TurnAsync(string turnId, CancellationToken ct) =>
            await FindTurnAsync(turnId, ct)
            ?? throw new InvalidOperationException($"Turn '{turnId}' not found in thread '{thread.Id}'.");

        public async Task<SessionTurn?> FindTurnAsync(string turnId, CancellationToken ct)
        {
            if (ThreadWorkingSet.FindResident(thread, turnId) is { } resident)
                return resident;
            if (_omitted.TryGetValue(turnId, out var omitted))
                return omitted;
            if (await owner.ReadTurnAsync(thread, turnId, ct) is not { } loaded)
                return null;
            _omitted[turnId] = loaded;
            return loaded;
        }

        public void Add(SessionTurn turn, SessionItem item)
        {
            turn.Items.Add(item);
            if (_omitted.ContainsKey(turn.Id))
                _records.Add(RolloutChangeRecords.ItemAppended(turn.Id, item));
        }

        public async Task PersistAsync(CancellationToken ct)
        {
            if (_records.Count > 0)
            {
                await owner.Persistence.AppendThreadChangesAsync(
                    thread,
                    [.. _records],
                    deleteUnreferencedAttachments: false,
                    ct);
                _records.Clear();
            }
            await owner.Persistence.SaveThreadAsync(thread, ct);
        }
    }
}
