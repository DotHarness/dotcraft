using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

public sealed partial class SessionService
{
    private sealed class MaintenanceCoordinator(SessionService owner)
    {
        public async Task<ThreadCompactResult> CompactAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Status != ThreadStatus.Active)
                throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot compact context.");
            if (thread.HistoryMode != HistoryMode.Server)
                throw new InvalidOperationException($"Thread '{threadId}' uses client-managed history and cannot be compacted by Session Core.");
            if (thread.Turns.Count == 0)
                throw new InvalidOperationException($"Thread '{threadId}' has no history to compact.");
            if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Wait for it to complete or cancel it first.");
            ThrowIfThreadMaintenanceActive(threadId);

            using var gateLock = await owner.Gate.AcquireAsync(threadId, ct);
            thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Wait for it to complete or cancel it first.");
            ThrowIfThreadMaintenanceActive(threadId);

            var maintenance = RegisterThreadMaintenance(threadId, "compacting");

            async Task<ThreadCompactResult> FinishAsync(ThreadCompactResult result)
            {
                maintenance.Dispose();
                await owner.TryStartNextQueuedTurnAsync(threadId, CancellationToken.None);
                return result;
            }

            using var linkedMaintenanceCts = CancellationTokenSource.CreateLinkedTokenSource(ct, maintenance.Token);
            var maintenanceCt = linkedMaintenanceCts.Token;

            try
            {
                await owner.EnsurePerThreadAgentIfMissingAsync(threadId, thread, maintenanceCt);
                var agent = owner.GetThreadAgentOrDefault(threadId);
                var session = await owner.Persistence.LoadOrCreateSessionAsync(agent, threadId, maintenanceCt);
                var pipeline = GetCompactionPipelineForThread(thread);
                var historyForEstimate = SnapshotSessionHistoryForConsolidation(session, thread);
                var tokenTracker = owner.AgentFactory.GetOrCreateTokenTracker(threadId);
                var usageEstimate = owner.EstimateContextTokens(
                    threadId,
                    historyForEstimate,
                    tokenTracker.LastInputTokens);
                var before = (int)Math.Min(int.MaxValue, usageEstimate.Tokens);
                var manualPromptSnapshot = owner.TryPrepareManualPromptRequestSnapshot(
                    threadId,
                    historyForEstimate,
                    before);
                var fallbackTools = manualPromptSnapshot?.Tools is { Count: > 0 }
                    ? null
                    : await RebuildCurrentThreadToolsForCompactionAsync(thread, maintenanceCt);
                var beforeThreshold = pipeline.EvaluateThreshold(before);
                var broker = owner.GetOrCreateBroker(threadId);

                broker.PublishSystemEvent(
                    "compacting",
                    percentLeft: beforeThreshold.PercentLeft,
                    tokenCount: beforeThreshold.Tokens);

                CompactionStatus status;
                try
                {
                    var compactResult = await pipeline.TryManualCompactHistoryAsync(
                        historyForEstimate,
                        threadId,
                        thread.LastActiveAt,
                        maintenanceCt,
                        inputTokenHint: before,
                        snapshot: manualPromptSnapshot,
                        fallbackTools: fallbackTools,
                        carryRequestOverhead: false);
                    status = compactResult.Status;
                    if (status.Success)
                    {
                        session.SetInMemoryChatHistory(
                            [.. compactResult.Messages],
                            jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
                        owner.InvalidatePromptRequestSnapshot(threadId, "manual_compaction");
                    }
                }
                catch (OperationCanceledException)
                {
                    broker.PublishSystemEvent(
                        "compactCancelled",
                        message: "cancelled",
                        percentLeft: beforeThreshold.PercentLeft,
                        tokenCount: beforeThreshold.Tokens);
                    return await FinishAsync(new ThreadCompactResult
                    {
                        Outcome = "cancelled",
                        Message = "cancelled",
                        ContextUsage = owner.TryGetContextUsageSnapshot(threadId)
                    });
                }
                catch (Exception ex)
                {
                    owner.Logger?.LogWarning(ex, "Manual compaction failed for thread {ThreadId}", threadId);
                    broker.PublishSystemEvent(
                        "compactFailed",
                        message: ex.Message,
                        percentLeft: beforeThreshold.PercentLeft,
                        tokenCount: beforeThreshold.Tokens);
                    return await FinishAsync(new ThreadCompactResult
                    {
                        Outcome = "failed",
                        Message = ex.Message,
                        ContextUsage = owner.TryGetContextUsageSnapshot(threadId)
                    });
                }

                switch (status.Outcome)
                {
                    case CompactionOutcome.Micro:
                    case CompactionOutcome.Partial:
                    {
                        tokenTracker.Reset();
                        await owner.Persistence.SaveSessionAsync(agent, session, threadId, maintenanceCt);
                        var contextUsage = await owner.SaveContextUsageSnapshotAsync(
                            threadId,
                            status.ThresholdAfter.Tokens,
                            maintenanceCt);
                        owner.ClearContextUsageAnchor(threadId);
                        owner.ReleaseStableContextPages(threadId);
                        if (status.Outcome == CompactionOutcome.Partial)
                            owner.TraceCollector?.RecordContextCompaction(threadId);

                        broker.PublishSystemEvent(
                            "compacted",
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: contextUsage);

                        if (status.Outcome == CompactionOutcome.Partial)
                            AppendManualCompactionNotice(thread, status, broker);
                        thread.LastActiveAt = DateTimeOffset.UtcNow;
                        await owner.PersistThreadWithMaterializationAsync(thread, maintenanceCt);
                        if (thread.Turns.LastOrDefault(t => t.Status == TurnStatus.Completed) is { } coveredTurn)
                        {
                            await TryAppendCompactionCheckpointAsync(
                                threadId,
                                coveredTurn.Id,
                                session,
                                new PendingCompactionCheckpoint(
                                    "manual",
                                    CompactionOutcomeToWire(status.Outcome),
                                    status.ThresholdBefore.Tokens,
                                    status.ThresholdAfter.Tokens),
                                maintenanceCt);
                        }

                        owner.ThreadRuntimeSignalForBroadcast?.Invoke(
                            threadId,
                            SessionThreadRuntimeSignal.ContextCompacted);

                        return await FinishAsync(new ThreadCompactResult
                        {
                            Outcome = CompactionOutcomeToWire(status.Outcome),
                            ContextUsage = contextUsage
                        });
                    }

                    case CompactionOutcome.Skipped:
                        broker.PublishSystemEvent(
                            "compactSkipped",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens);
                        return await FinishAsync(new ThreadCompactResult
                        {
                            Outcome = "skipped",
                            Message = status.FailureReason,
                            ContextUsage = owner.TryGetContextUsageSnapshot(threadId)
                        });

                    case CompactionOutcome.Failed:
                        broker.PublishSystemEvent(
                            "compactFailed",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens);
                        return await FinishAsync(new ThreadCompactResult
                        {
                            Outcome = "failed",
                            Message = status.FailureReason,
                            ContextUsage = owner.TryGetContextUsageSnapshot(threadId)
                        });

                    default:
                        return await FinishAsync(new ThreadCompactResult
                        {
                            Outcome = CompactionOutcomeToWire(status.Outcome),
                            Message = status.FailureReason,
                            ContextUsage = owner.TryGetContextUsageSnapshot(threadId)
                        });
                }
            }
            finally
            {
                maintenance.Dispose();
            }
        }

        public async Task<ThreadMemoryConsolidationResult> ConsolidateMemoryAsync(
            string threadId,
            CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            if (thread.Status != ThreadStatus.Active)
                throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot consolidate memory.");
            if (thread.HistoryMode != HistoryMode.Server)
                throw new InvalidOperationException($"Thread '{threadId}' uses client-managed history and cannot be consolidated by Session Core.");
            if (thread.Turns.Count == 0)
                throw new InvalidOperationException($"Thread '{threadId}' has no history to consolidate.");
            if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Wait for it to complete or cancel it first.");
            ThrowIfThreadMaintenanceActive(threadId);

            IReadOnlyList<ChatMessage> history;
            SessionTurn completedTurn;
            PromptRequestSnapshot? requestSnapshot;
            ThreadMaintenanceRegistration maintenance;
            using (await owner.Gate.AcquireAsync(threadId, ct))
            {
                thread = await owner.GetOrLoadThreadAsync(threadId, ct);
                if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
                    throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Wait for it to complete or cancel it first.");
                ThrowIfThreadMaintenanceActive(threadId);

                completedTurn = thread.Turns.LastOrDefault(t => t.Status == TurnStatus.Completed)
                    ?? throw new InvalidOperationException($"Thread '{threadId}' has no completed turn to consolidate.");

                await owner.EnsurePerThreadAgentIfMissingAsync(threadId, thread, ct);
                var agent = owner.GetThreadAgentOrDefault(threadId);
                var session = await owner.Persistence.LoadOrCreateSessionAsync(agent, threadId, ct);
                history = SnapshotSessionHistoryForConsolidation(session, thread);
                if (history.Count == 0)
                    throw new InvalidOperationException($"Thread '{threadId}' has no model-visible history to consolidate.");

                requestSnapshot = owner.TryGetValidLastPromptRequestSnapshot(threadId, history);
                maintenance = RegisterThreadMaintenance(threadId, "consolidating");
                if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                    runtime.ResetTurnsSinceConsolidation();
            }

            var broker = owner.GetOrCreateBroker(threadId);
            broker.PublishSystemEvent("consolidating");

            using var linkedMaintenanceCts = CancellationTokenSource.CreateLinkedTokenSource(ct, maintenance.Token);
            try
            {
                return await RunMemoryConsolidationAsync(
                    threadId,
                    thread,
                    completedTurn,
                    history,
                    requestSnapshot,
                    () => completedTurn.Items.Count + 1,
                    broker,
                    linkedMaintenanceCts.Token);
            }
            finally
            {
                maintenance.Dispose();
                await owner.TryStartNextQueuedTurnAsync(threadId, CancellationToken.None);
            }
        }

        public CompactionPipeline GetCompactionPipelineForThread(string threadId)
        {
            owner._runtimeRegistry.TryGetThread(threadId, out var thread);
            return GetCompactionPipelineForThread(threadId, thread);
        }

        public CompactionPipeline GetCompactionPipelineForThread(SessionThread thread) =>
            GetCompactionPipelineForThread(thread.Id, thread);

        public CompactionPipeline GetCompactionPipelineForThread(string threadId, SessionThread? thread) =>
            owner.AgentFactory.GetCompactionPipeline(
                threadId,
                thread?.Configuration?.ProviderId,
                thread?.Configuration?.Model,
                owner._appConfigMonitor?.Current ?? owner.AgentFactory.ToolProviderContext.Config);

        public void CompleteThreadMaintenance(string threadId, ThreadMaintenanceState state)
        {
            if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime)
                && runtime.TryClearMaintenance(state))
            {
                owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.MaintenanceCompleted);
            }

            state.Dispose();
        }

        public void ThrowIfThreadMaintenanceActive(string threadId)
        {
            if (owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime)
                && runtime.Maintenance is { } maintenance)
            {
                throw new InvalidOperationException(
                    $"Thread '{threadId}' has active thread maintenance ({maintenance.Kind}). Wait for it to complete or cancel it first.");
            }
        }

        public bool TryScheduleMemoryConsolidation(
            string threadId,
            SessionThread thread,
            SessionTurn turn,
            AgentSession session,
            SessionEventChannel eventChannel,
            Func<int> nextItemSequence)
        {
            if (ThreadVisibility.IsInternal(thread))
                return false;

            var memoryConfig = owner._appConfigMonitor?.Current.Memory
                ?? owner.AgentFactory.ToolProviderContext.Config.Memory;

            if (!memoryConfig.AutoConsolidateEnabled)
                return false;

            var interval = Math.Max(1, memoryConfig.ConsolidateEveryNTurns);
            if (!owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                return false;

            var count = runtime.IncrementTurnsSinceConsolidation();

            if (count < interval)
                return false;

            var history = SnapshotSessionHistoryForConsolidation(session, thread);
            if (history.Count == 0)
                return false;
            var requestSnapshot = owner.TryGetValidLastPromptRequestSnapshot(
                threadId,
                history,
                invalidateOnMismatch: false);
            // Some providers/session adapters persist a normalized consolidation
            // history that cannot byte-match the just-sent request prefix. A
            // snapshot captured by this same completed turn is still fresh because
            // no compaction boundary has crossed it yet.
            if (requestSnapshot is null
                && owner.TryGetLastPromptRequestSnapshot(threadId) is { } freshTurnSnapshot
                && string.Equals(freshTurnSnapshot.TurnId, turn.Id, StringComparison.Ordinal))
            {
                requestSnapshot = freshTurnSnapshot;
            }

            var work = new AutoMemoryConsolidationWork(
                thread,
                turn,
                history,
                requestSnapshot,
                nextItemSequence);
            return TryStartAutoMemoryConsolidation(threadId, work, eventChannel);
        }

        public async Task TryAppendCompactionCheckpointAsync(
            string threadId,
            string coveredThroughTurnId,
            AgentSession session,
            PendingCompactionCheckpoint checkpoint,
            CancellationToken ct)
        {
            if (owner.IsPendingPermanentDeletion(threadId))
                return;

            if (!TrySnapshotInMemoryHistory(session, out var history))
                return;

            try
            {
                await owner.Persistence.AppendCompactionCheckpointAsync(
                    threadId,
                    coveredThroughTurnId,
                    history,
                    checkpoint.Trigger,
                    checkpoint.Mode,
                    checkpoint.TokensBefore,
                    checkpoint.TokensAfter,
                    ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                owner.Logger?.LogWarning(ex, "Failed to append compaction checkpoint for thread {ThreadId}", threadId);
            }
        }

        public static SessionItem CreateCompactionNoticeItem(
            SessionTurn turn,
            int seq,
            string trigger,
            CompactionStatus status)
        {
            var mode = status.Outcome == CompactionOutcome.Micro ? "micro" : "partial";
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
                    Kind = "compacted",
                    Trigger = trigger,
                    Mode = mode,
                    TokensBefore = status.ThresholdBefore.Tokens,
                    TokensAfter = status.ThresholdAfter.Tokens,
                    PercentLeftAfter = status.ThresholdAfter.PercentLeft,
                    ClearedToolResults = status.ClearedToolResults
                }
            };
        }

        public static string CompactionOutcomeToWire(CompactionOutcome outcome) =>
            outcome switch
            {
                CompactionOutcome.Micro => "micro",
                CompactionOutcome.Partial => "partial",
                CompactionOutcome.Skipped => "skipped",
                CompactionOutcome.Failed => "failed",
                _ => outcome.ToString().ToLowerInvariant()
            };

        private async Task<IReadOnlyList<AITool>> RebuildCurrentThreadToolsForCompactionAsync(
            SessionThread thread,
            CancellationToken ct)
        {
            if (owner.RequiresPerThreadAgent(thread) || owner._forcePerThreadAgents)
            {
                await owner.EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);
                if (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
                    && runtime.CurrentTools is { } threadTools)
                    return threadTools;
            }

            var config = thread.Configuration ?? new ThreadConfiguration();
            var mode = config.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
                ? AgentMode.Plan
                : AgentMode.Agent;
            var tools = owner.AgentFactory.CreateToolsForMode(mode);
            owner.AppendChannelTools(tools, thread);
            ApplyThreadToolFilters(tools, config);
            return tools;
        }

        private ThreadMaintenanceRegistration RegisterThreadMaintenance(string threadId, string kind)
        {
            var state = new ThreadMaintenanceState(kind);
            if (!owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime)
                || !runtime.TrySetMaintenance(state))
            {
                state.Dispose();
                throw new InvalidOperationException(
                    $"Thread '{threadId}' has active thread maintenance. Wait for it to complete or cancel it first.");
            }

            owner.ThreadRuntimeSignalForBroadcast?.Invoke(
                threadId,
                kind == "compacting"
                    ? SessionThreadRuntimeSignal.MaintenanceCompactingStarted
                    : SessionThreadRuntimeSignal.MaintenanceConsolidatingStarted);
            return new ThreadMaintenanceRegistration(owner, threadId, state);
        }

        private bool TryStartAutoMemoryConsolidation(
            string threadId,
            AutoMemoryConsolidationWork work,
            SessionEventChannel? eventChannel)
        {
            if (!owner._runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                return false;

            if (!runtime.TryStartAutoMemoryConsolidation())
            {
                runtime.SetPendingAutoMemoryConsolidation(work);
                return true;
            }

            runtime.ResetTurnsSinceConsolidation();
            eventChannel?.EmitSystemEvent("consolidating");

            _ = Task.Run(async () =>
            {
                var current = work;
                var broker = owner.GetOrCreateBroker(threadId);
                try
                {
                    while (true)
                    {
                        await RunMemoryConsolidationAsync(
                            threadId,
                            current.Thread,
                            current.Turn,
                            current.History,
                            current.RequestSnapshot,
                            current.NextItemSequence,
                            broker,
                            CancellationToken.None);

                        if (runtime.TryTakePendingAutoMemoryConsolidation(out current))
                        {
                            runtime.ResetTurnsSinceConsolidation();
                            broker.PublishTurnSystemEvent(current.Turn.Id, "consolidating");
                            continue;
                        }

                        runtime.CompleteAutoMemoryConsolidation();

                        if (runtime.TryTakePendingAutoMemoryConsolidation(out current))
                        {
                            if (runtime.TryStartAutoMemoryConsolidation())
                            {
                                runtime.ResetTurnsSinceConsolidation();
                                broker.PublishTurnSystemEvent(current.Turn.Id, "consolidating");
                                continue;
                            }

                            runtime.SetPendingAutoMemoryConsolidation(current);
                        }

                        break;
                    }
                }
                catch (Exception ex)
                {
                    owner.Logger?.LogWarning(ex, "Automatic memory consolidation runner failed for thread {ThreadId}", threadId);
                    runtime.CompleteAutoMemoryConsolidation();
                }
            });

            return true;
        }

        private async Task<ThreadMemoryConsolidationResult> RunMemoryConsolidationAsync(
            string threadId,
            SessionThread thread,
            SessionTurn turn,
            IReadOnlyList<ChatMessage> history,
            PromptRequestSnapshot? requestSnapshot,
            Func<int> nextItemSequence,
            ThreadEventBroker broker,
            CancellationToken ct)
        {
            var currentConfig = owner._appConfigMonitor?.Current ?? owner.AgentFactory.ToolProviderContext.Config;
            var consolidator = owner.AgentFactory.CreateConsolidatorForRuntime(
                currentConfig,
                thread.Configuration?.ProviderId,
                thread.Configuration?.Model);
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

            try
            {
                var result = consolidator is IMemoryForkConsolidator forkConsolidator
                    ? await forkConsolidator.ConsolidateAsync(history, requestSnapshot, ct)
                    : await consolidator.ConsolidateAsync(history, ct);
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
                        owner.ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.MemoryConsolidated);
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
                broker.PublishSystemEvent("consolidationCancelled", message: "cancelled");
                return new ThreadMemoryConsolidationResult
                {
                    Outcome = "cancelled",
                    Message = "cancelled"
                };
            }
            catch (Exception ex)
            {
                owner.Logger?.LogWarning(ex, "Memory consolidation failed for thread {ThreadId}", threadId);
                broker.PublishSystemEvent("consolidationFailed", message: ex.Message);
                return new ThreadMemoryConsolidationResult
                {
                    Outcome = "failed",
                    Message = ex.Message
                };
            }
        }

        private static IReadOnlyList<ChatMessage> SnapshotSessionHistoryForConsolidation(
            AgentSession session,
            SessionThread thread)
        {
            var chatHistory = session.GetService<ChatHistoryProvider>();
            if (chatHistory is InMemoryChatHistoryProvider provider)
            {
                var messages = provider.GetMessages(session).ToList();
                if (messages.Count > 0)
                    return messages;
            }

            var fallback = new List<ChatMessage>();
            foreach (var turn in thread.Turns)
            {
                foreach (var item in turn.Items)
                {
                    if (item.Type == ItemType.UserMessage && item.AsUserMessage is { Text: { } userText } &&
                        !string.IsNullOrWhiteSpace(userText))
                    {
                        fallback.Add(new ChatMessage(ChatRole.User, userText.Trim()));
                    }
                    else if (item.Type == ItemType.AgentMessage && item.AsAgentMessage is { Text: { } agentText } &&
                             !string.IsNullOrWhiteSpace(agentText))
                    {
                        fallback.Add(new ChatMessage(ChatRole.Assistant, agentText.Trim()));
                    }
                }
            }

            return fallback;
        }

        private static void AppendManualCompactionNotice(
            SessionThread thread,
            CompactionStatus status,
            ThreadEventBroker broker)
        {
            var turn = thread.Turns.LastOrDefault(t => t.Status == TurnStatus.Completed);
            if (turn is null)
                return;

            var noticeItem = CreateCompactionNoticeItem(
                turn,
                turn.Items.Count + 1,
                trigger: "manual",
                status);
            turn.Items.Add(noticeItem);
            broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, noticeItem);
            broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, noticeItem);
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
