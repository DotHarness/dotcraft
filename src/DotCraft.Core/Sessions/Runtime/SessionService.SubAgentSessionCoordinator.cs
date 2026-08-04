using DotCraft.Channels;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class SubAgentSessionCoordinator(SessionService owner)
    {
        public async Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.UpsertThreadSpawnEdgeAsync(edge, ct);
            var rootThreadId = await ResolveRootThreadIdAsync(edge.ChildThreadId, ct);
            owner._subAgentCommunicationRuntime.PublishGraph(rootThreadId);
            owner.SubAgentGraphChangedForBroadcast?.Invoke(edge.ParentThreadId, edge.ChildThreadId);
        }

        public async Task SetThreadSpawnEdgeStatusAsync(
            string parentThreadId,
            string childThreadId,
            string status,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);
            var rootThreadId = await ResolveRootThreadIdAsync(childThreadId, ct);
            owner._subAgentCommunicationRuntime.PublishGraph(rootThreadId);
            owner.SubAgentGraphChangedForBroadcast?.Invoke(parentThreadId, childThreadId);
        }

        public Task<IReadOnlyList<ThreadSpawnEdge>> ListChildrenAsync(
            string parentThreadId,
            bool includeClosed,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return owner.Persistence.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);
        }

        public async Task AddMailboxEntryAsync(SubAgentMailboxEntry entry, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.AddSubAgentMailboxEntryAsync(entry, ct);
            owner._subAgentCommunicationRuntime.PublishMailbox(entry.RootThreadId, entry.TargetAgentPath);
        }

        public Task<IReadOnlyList<SubAgentMailboxEntry>> ListPendingMailboxAsync(
            string rootThreadId,
            string targetAgentPath,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return owner.Persistence.ListPendingSubAgentMailboxAsync(rootThreadId, targetAgentPath, ct);
        }

        public async Task MarkMailboxDeliveredAsync(
            string rootThreadId,
            IReadOnlyList<string> entryIds,
            DateTimeOffset deliveredAt,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await owner.Persistence.MarkSubAgentMailboxDeliveredAsync(rootThreadId, entryIds, deliveredAt, ct);
        }

        private async Task<string> ResolveRootThreadIdAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            return string.IsNullOrWhiteSpace(thread.Source.SubAgent?.RootThreadId)
                ? thread.Id
                : thread.Source.SubAgent.RootThreadId;
        }

        public async Task<SessionTurn> StartSyntheticTurnAsync(
            string threadId,
            IList<AIContent> content,
            string runtimeType,
            string? profileName,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Status != ThreadStatus.Active)
                throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");

            if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                throw new InvalidOperationException($"Thread '{threadId}' already has a running Turn. Wait for it to complete or cancel it first.");

            var channelInfo = ChannelSessionScope.Current;
            var turnOriginChannel = channelInfo?.Channel ?? thread.OriginChannel;
            var turnChannelContext = channelInfo?.DefaultDeliveryTarget ?? thread.ChannelContext;
            var triggerInfo = TurnTriggerScope.Current;
            var text = string.Concat(content.OfType<TextContent>().Select(t => t.Text));
            var turn = new SessionTurn
            {
                Id = SessionIdGenerator.NewTurnId(SessionIdGenerator.ReserveNextTurnSequence(thread)),
                ThreadId = threadId,
                Status = TurnStatus.Running,
                StartedAt = DateTimeOffset.UtcNow,
                OriginChannel = turnOriginChannel,
                Initiator = new TurnInitiatorContext
                {
                    ChannelName = turnOriginChannel,
                    UserId = channelInfo?.UserId ?? thread.UserId,
                    ChannelContext = turnChannelContext,
                    GroupId = channelInfo?.GroupId
                }
            };

            var userItem = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(1),
                TurnId = turn.Id,
                Type = ItemType.UserMessage,
                Status = ItemStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Payload = new UserMessagePayload
                {
                    Text = text,
                    ChannelName = turnOriginChannel,
                    ChannelContext = turnChannelContext,
                    GroupId = channelInfo?.GroupId,
                    TriggerKind = triggerInfo?.Kind,
                    TriggerLabel = triggerInfo?.Label,
                    TriggerRefId = triggerInfo?.RefId
                }
            };

            turn.Input = userItem;
            turn.Items.Add(userItem);
            thread.Turns.Add(turn);
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            thread.Metadata["subagent.syntheticRuntime"] = runtimeType;
            if (!string.IsNullOrWhiteSpace(profileName))
                thread.Metadata["subagent.profileName"] = profileName;

            var broker = owner.GetOrCreateBroker(threadId);
            broker.PublishTurnStarted(turn);
            owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnStarted);
            broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, userItem);
            broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, userItem);
            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            return turn;
        }

        public async Task<SessionTurn> CompleteSyntheticTurnAsync(
            string threadId,
            string turnId,
            string text,
            bool isError,
            SubAgentTokenUsage? tokensUsed,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            var turn = thread.Turns.FirstOrDefault(t => string.Equals(t.Id, turnId, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Turn '{turnId}' not found in thread '{threadId}'.");
            if (turn.Status is not (TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                return turn;

            var item = isError
                ? CreateErrorItem(turn, turn.Items.Count + 1, text, "subagent_error", fatal: true)
                : new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
                    TurnId = turn.Id,
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new AgentMessagePayload { Text = text }
                };

            turn.Items.Add(item);
            turn.Status = isError ? TurnStatus.Failed : TurnStatus.Completed;
            turn.CompletedAt = DateTimeOffset.UtcNow;
            turn.Error = isError ? text : null;
            if (tokensUsed != null)
            {
                turn.TokenUsage = new TokenUsageInfo
                {
                    InputTokens = tokensUsed.InputTokens,
                    OutputTokens = tokensUsed.OutputTokens,
                    CachedInputTokens = tokensUsed.CachedInputTokens,
                    CacheWriteInputTokens = tokensUsed.CacheWriteInputTokens,
                    ReasoningOutputTokens = tokensUsed.ReasoningOutputTokens,
                    LlmCallCount = tokensUsed.InputTokens > 0 || tokensUsed.OutputTokens > 0 ? 1 : 0,
                    TotalTokens = tokensUsed.InputTokens + tokensUsed.OutputTokens
                };
            }

            thread.LastActiveAt = DateTimeOffset.UtcNow;
            var broker = owner.GetOrCreateBroker(threadId);
            broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, item);
            broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, item);
            if (isError)
            {
                broker.PublishTurnFailed(turn, text);
                owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnFailed);
            }
            else
            {
                broker.PublishTurnCompleted(turn);
                owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCompleted);
            }

            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            return turn;
        }

        public async Task<SessionTurn> CancelSyntheticTurnAsync(
            string threadId,
            string turnId,
            string reason,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            var turn = thread.Turns.FirstOrDefault(t => string.Equals(t.Id, turnId, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Turn '{turnId}' not found in thread '{threadId}'.");
            if (turn.Status is not (TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                return turn;

            turn.Status = TurnStatus.Cancelled;
            turn.CompletedAt = DateTimeOffset.UtcNow;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            var broker = owner.GetOrCreateBroker(threadId);
            broker.PublishTurnCancelled(turn, reason);
            owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
            await owner.PersistThreadWithMaterializationAsync(thread, ct);
            return turn;
        }
    }
}
