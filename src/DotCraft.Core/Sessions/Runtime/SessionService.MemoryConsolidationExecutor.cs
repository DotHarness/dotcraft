using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace DotCraft.Sessions;

internal sealed record AutoMemoryConsolidationWork(
    SessionThread Thread,
    SessionTurn Turn,
    IReadOnlyList<ChatMessage> History,
    PromptRequestSnapshot? RequestSnapshot,
    Func<int> NextItemSequence,
    ProviderConversationIdentity Identity);

public sealed partial class SessionService
{
    private sealed class MemoryConsolidationExecutor(SessionService owner)
    {
        public async Task<ThreadMemoryConsolidationResult> RunAsync(
            string threadId,
            SessionThread thread,
            SessionTurn turn,
            IReadOnlyList<ChatMessage> history,
            PromptRequestSnapshot? requestSnapshot,
            Func<int> nextItemSequence,
            ThreadEventBroker broker,
            CancellationToken ct,
            ProviderConversationIdentity? workIdentity = null)
        {
            var identity = workIdentity ?? ThreadConversationIdentity.Create(thread, turn,
                owner.GetOrCreateResponsesContextWindow(threadId).CurrentWindowId, ProviderRequestKind.Memory);
            using var requestScope = new AuxiliaryProviderRequestScope(identity, owner.TraceCollector);
            using var retryScope = ModelStreamRetryRuntimeScope.Suppress();
            var started = Stopwatch.GetTimestamp();
            var status = "failed";
            try
            {
                var currentConfig = owner._appConfigMonitor?.Current ?? owner.AgentFactory.RuntimeContext.Config;
                var consolidator = owner.AgentFactory.CreateConsolidatorForRuntime(
                    currentConfig,
                    thread.Configuration?.ProviderId,
                    thread.Configuration?.Model,
                    thread.Configuration?.ContextWindow?.Mode ?? ContextWindowMode.Default);
                if (consolidator is null)
                {
                    const string message = "memory_consolidator_unavailable";
                    broker.PublishSystemEvent("consolidationFailed", message: message);
                    return new ThreadMemoryConsolidationResult
                    {
                        Outcome = "failed",
                        Message = message
                    };
                }

                var result = consolidator is IMemoryForkConsolidator forkConsolidator
                    ? await forkConsolidator.ConsolidateAsync(history, requestSnapshot, ct)
                    : await consolidator.ConsolidateAsync(history, ct);
                status = result.Outcome.ToString().ToLowerInvariant();
                switch (result.Outcome)
                {
                    case MemoryConsolidationOutcome.Succeeded:
                        await AppendMemoryConsolidationNoticeAsync(
                            threadId,
                            thread,
                            turn,
                            nextItemSequence,
                            broker,
                            ct);
                        if (result.MemoryWritten)
                            owner.MarkMemoryContextDirty();
                        owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.MemoryConsolidated, null);
                        broker.PublishSystemEvent("consolidated");
                        return new ThreadMemoryConsolidationResult
                        {
                            Outcome = "succeeded",
                            MemoryWritten = result.MemoryWritten,
                            HistoryWritten = result.HistoryWritten
                        };

                    case MemoryConsolidationOutcome.Skipped:
                        broker.PublishSystemEvent("consolidationSkipped", message: result.Message);
                        return new ThreadMemoryConsolidationResult
                        {
                            Outcome = "skipped",
                            Message = result.Message,
                            MemoryWritten = result.MemoryWritten,
                            HistoryWritten = result.HistoryWritten
                        };

                    case MemoryConsolidationOutcome.Failed:
                        owner.Logger?.LogWarning(
                            "Memory consolidation failed for thread {ThreadId}: {Message}",
                            threadId,
                            result.Message);
                        broker.PublishSystemEvent("consolidationFailed", message: result.Message);
                        return new ThreadMemoryConsolidationResult
                        {
                            Outcome = "failed",
                            Message = result.Message,
                            MemoryWritten = result.MemoryWritten,
                            HistoryWritten = result.HistoryWritten
                        };

                    default:
                        var outcome = result.Outcome.ToString().ToLowerInvariant();
                        broker.PublishSystemEvent("consolidationFailed", message: outcome);
                        return new ThreadMemoryConsolidationResult
                        {
                            Outcome = "failed",
                            Message = outcome
                        };
                }
            }
            catch (OperationCanceledException)
            {
                status = "cancelled";
                broker.PublishSystemEvent("consolidationCancelled", message: "cancelled");
                return new ThreadMemoryConsolidationResult
                {
                    Outcome = "cancelled",
                    Message = "cancelled"
                };
            }
            catch (Exception ex)
            {
                status = "failed";
                owner.Logger?.LogWarning(ex, "Memory consolidation failed for thread {ThreadId}", threadId);
                broker.PublishSystemEvent("consolidationFailed", message: ex.Message);
                return new ThreadMemoryConsolidationResult
                {
                    Outcome = "failed",
                    Message = ex.Message
                };
            }
            finally
            {
                ((IModelRuntimeDiagnostics?)owner.TraceCollector)?.Record(new ModelRuntimeDiagnostic(
                    "maintenance.request", new Dictionary<string, object?>
                    {
                        ["sessionKey"] = threadId,
                        ["requestKind"] = "memory",
                        ["turnId"] = identity.TurnId,
                        ["messageCount"] = history.Count,
                        ["status"] = status,
                        ["durationMs"] = Stopwatch.GetElapsedTime(started).TotalMilliseconds
                    }));
            }
        }

        private static SessionItem CreateMemoryConsolidationNoticeItem(SessionTurn turn, int seq)
        {
            return new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(seq),
                TurnId = turn.Id,
                Type = ItemType.SystemNotice,
                Status = ItemStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Payload = new SystemNoticePayload
                {
                    Kind = "memoryConsolidated"
                }
            };
        }

        private async Task AppendMemoryConsolidationNoticeAsync(
            string threadId,
            SessionThread thread,
            SessionTurn turn,
            Func<int> nextItemSequence,
            ThreadEventBroker broker,
            CancellationToken ct = default)
        {
            if (owner.IsPendingPermanentDeletion(threadId))
                return;

            using var gateLock = await owner.Gate.AcquireAsync(threadId, ct);
            if (owner.IsPendingPermanentDeletion(threadId))
                return;

            var noticeItem = CreateMemoryConsolidationNoticeItem(turn, nextItemSequence());
            turn.Items.Add(noticeItem);
            broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, noticeItem);
            broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, noticeItem);
            await owner.PersistThreadWithMaterializationAsync(thread, ct);
        }
    }
}
