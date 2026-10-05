using DotCraft.Agents;
using DotCraft.Hooks;
using Microsoft.Extensions.AI;
using DotCraft.Sessions.Wire;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed record PreparedGuidance(
        QueuedTurnInput Input, IList<AIContent> Content, string DisplayText, HookResult Hook);

    private static bool IsPendingGuidance(QueuedTurnInput input, string turnId) =>
        string.Equals(input.Status, "guidancePending", StringComparison.Ordinal)
        && string.Equals(input.ReadyAfterTurnId, turnId, StringComparison.Ordinal);

    private async Task<bool> HasPendingGuidanceAsync(SessionThread thread, string turnId, CancellationToken ct)
    {
        using (await AcquireThreadQueueLockAsync(thread.Id, ct))
            return thread.QueuedInputs.Any(input =>
                IsPendingGuidance(input, turnId) && !IsLegacyGoalBudgetGuidanceInput(input));
    }

    private async Task<IReadOnlyList<ChatMessage>> AdmitGuidanceInputsAsync(
        ThreadRuntime runtime, SessionTurn turn, SessionEventChannel eventChannel,
        Func<int> nextItemSeq, Action finalizeStreamingAgentMessage, Action finalizeStreamingReasoning,
        TurnModelHistory turnModelHistory, CancellationToken drainCt)
    {
        var thread = runtime.Thread;
        if (!_runtimeRegistry.IsCurrent(thread.Id, runtime))
            return [];

        var pending = await runtime.Commands.InvokeAsync(
            commandCt => ListPendingGuidanceAsync(thread, turn.Id, commandCt),
            drainCt);
        var prepared = new List<PreparedGuidance>();
        foreach (var queued in pending)
        {
            var content = await ThreadQueue.ResolveInputPartsAsync(queued.MaterializedInputParts.ToList(), drainCt);
            if (content.Count == 0)
                continue;

            var displayText = !string.IsNullOrWhiteSpace(queued.DisplayText)
                ? queued.DisplayText
                : SessionWireMapper.BuildDisplayText(queued.NativeInputParts);
            var hook = await RunPromptLifecycleHookAsync(
                HookEvent.UserPromptSubmit,
                thread.Id,
                turn.Id,
                thread.WorkspacePath,
                displayText,
                string.Equals(queued.TriggerKind, "hook", StringComparison.Ordinal),
                drainCt);
            prepared.Add(new PreparedGuidance(queued, content, displayText, hook));
        }

        if (prepared.Count == 0 || !_runtimeRegistry.IsCurrent(thread.Id, runtime))
            return [];

        return await runtime.Commands.InvokeAsync(
            _ => StageGuidanceInputsAsync(thread, turn, eventChannel, nextItemSeq,
                finalizeStreamingAgentMessage, finalizeStreamingReasoning, turnModelHistory, prepared),
            drainCt);
    }

    private async Task<IReadOnlyList<QueuedTurnInput>> ListPendingGuidanceAsync(
        SessionThread thread, string turnId, CancellationToken ct)
    {
        List<QueuedTurnInput> pending;
        IReadOnlyList<QueuedTurnInput>? cleanupSnapshot = null;
        using (await AcquireThreadQueueLockAsync(thread.Id, ct))
        {
            var queue = thread.QueuedInputs.ToList();
            if (queue.RemoveAll(IsLegacyGoalBudgetGuidanceInput) > 0)
            {
                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, ct);
                cleanupSnapshot = queue.ToList();
            }

            pending = queue.Where(input => IsPendingGuidance(input, turnId)).ToList();
        }
        if (cleanupSnapshot != null)
            PublishQueueUpdated(thread.Id, cleanupSnapshot);
        return pending;
    }

    private async Task<IReadOnlyList<ChatMessage>> StageGuidanceInputsAsync(
        SessionThread thread, SessionTurn turn, SessionEventChannel eventChannel,
        Func<int> nextItemSeq, Action finalizeStreamingAgentMessage, Action finalizeStreamingReasoning,
        TurnModelHistory turnModelHistory, IReadOnlyList<PreparedGuidance> prepared)
    {
        var queueLease = await AcquireThreadQueueLockAsync(thread.Id, CancellationToken.None);
        var staged = false;
        try
        {
            var current = prepared
                .Where(guidance => thread.QueuedInputs.Any(input =>
                    string.Equals(input.Id, guidance.Input.Id, StringComparison.Ordinal)
                    && IsPendingGuidance(input, turn.Id)))
                .ToList();
            var blocked = current.Where(guidance => guidance.Hook.Blocked).ToList();
            if (blocked.Count > 0)
            {
                var blockedIds = blocked.Select(guidance => guidance.Input.Id).ToHashSet(StringComparer.Ordinal);
                thread.QueuedInputs = thread.QueuedInputs.Where(input => !blockedIds.Contains(input.Id)).ToList();
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                PublishQueueUpdated(thread.Id, thread.QueuedInputs.ToList());
                foreach (var guidance in blocked)
                {
                    var reason = string.IsNullOrWhiteSpace(guidance.Hook.BlockReason)
                        ? "no reason given"
                        : guidance.Hook.BlockReason;
                    eventChannel.EmitSystemEvent(
                        "guidanceBlocked",
                        $"Message blocked by hook: {reason}",
                        messageKey: "system.guidanceBlocked",
                        parameters: new Dictionary<string, object?> { ["reason"] = reason });
                }
            }

            var admitted = current.Where(guidance => !guidance.Hook.Blocked).ToList();
            if (admitted.Count == 0)
                return [];

            finalizeStreamingAgentMessage();
            finalizeStreamingReasoning();

            var messages = new List<ChatMessage>();
            var inputs = new List<TurnModelHistory.StagedInput>();
            var items = new List<SessionItem>();
            foreach (var guidance in admitted)
            {
                var item = CreateGuidanceItem(turn, guidance, nextItemSeq());
                var message = new ChatMessage(ChatRole.User, guidance.Content);
                items.Add(item);
                inputs.Add(new TurnModelHistory.StagedInput(message, item.Id, [guidance.Input.Id]));
                messages.Add(message);
                if (ExtractHookContext(guidance.Hook) is { } hookContext)
                {
                    messages.Add(new ChatMessage(ChatRole.User, StreamingFunctionInvokingChatClient.BuildHookFeedbackReminder(
                        [new StreamingToolHookFeedback(nameof(HookEvent.UserPromptSubmit), hookContext.Trim(), false)])));
                }
            }

            var admittedIds = admitted.Select(guidance => guidance.Input.Id).ToHashSet(StringComparer.Ordinal);
            turnModelHistory.Stage(inputs, async () =>
            {
                turn.Items.AddRange(items);
                thread.QueuedInputs = thread.QueuedInputs.Where(input => !admittedIds.Contains(input.Id)).ToList();
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                foreach (var item in items)
                {
                    eventChannel.EmitItemStarted(item);
                    eventChannel.EmitItemCompleted(item);
                }
                PublishQueueUpdated(thread.Id, thread.QueuedInputs.ToList());
            }, queueLease);
            staged = true;
            return messages;
        }
        finally { if (!staged) queueLease.Dispose(); }
    }

    private static SessionItem CreateGuidanceItem(SessionTurn turn, PreparedGuidance guidance, int itemSeq)
    {
        var queued = guidance.Input;
        var images = ExtractUserMessageImages(guidance.Content);
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(itemSeq),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = guidance.DisplayText,
                DeliveryMode = "guidance",
                ClientUserMessageId = queued.ClientUserMessageId,
                NativeInputParts = queued.NativeInputParts.ToList(),
                MaterializedInputParts = queued.MaterializedInputParts.ToList(),
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
