using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using DotCraft.Sessions.Wire;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;

namespace DotCraft.Sessions;

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
                var session = await owner.Persistence.LoadModelHistoryAsync(threadId, maintenanceCt);
                var coordinator = GetCompactionCoordinatorForThread(thread);
                var historyForEstimate = SessionService.PrepareProviderVisibleHistory(
                    SnapshotSessionHistoryForConsolidation(session, thread));
                var tokenTracker = owner.AgentFactory.GetOrCreateTokenTracker(threadId);
                var manualPromptSnapshot = owner.TryPrepareManualPromptRequestSnapshot(
                    threadId,
                    historyForEstimate,
                    estimatedInputTokens: null);
                var preparedEstimate = owner.PrepareContextTokenEstimate(
                    threadId,
                    historyForEstimate,
                    tokenTracker.LastContextTokens,
                    manualPromptSnapshot);
                historyForEstimate = preparedEstimate.History;
                manualPromptSnapshot = preparedEstimate.RequestSnapshot;
                var usageEstimate = preparedEstimate.Estimate;
                var before = (int)Math.Min(int.MaxValue, usageEstimate.Tokens);
                if (manualPromptSnapshot is not null)
                    manualPromptSnapshot = manualPromptSnapshot with { EstimatedInputTokens = before };
                var fallbackTools = manualPromptSnapshot?.Tools is { Count: > 0 }
                    ? null
                    : await RebuildCurrentThreadToolsForCompactionAsync(thread, maintenanceCt);
                var beforeThreshold = coordinator.EvaluateThreshold(before);
                var beforeUsage = owner.CreateContextUsageSnapshot(
                    threadId,
                    before,
                    usageEstimate.Source,
                    usageEstimate.IsEstimate);
                var broker = owner.GetOrCreateBroker(threadId);
                var coveredTurnId = thread.Turns.LastOrDefault(t => t.Status == TurnStatus.Completed)?.Id;
                var preCompactHook = await owner.RunCompactionHookAsync(
                    HookEvent.PreCompact,
                    thread,
                    coveredTurnId,
                    "manual",
                    beforeThreshold,
                    thresholdAfter: null,
                    beforeUsage,
                    outcome: null,
                    maintenanceCt);
                if (preCompactHook.Blocked)
                {
                    var message = BuildHookBlockedMessage("Context compaction", preCompactHook);
                    broker.PublishSystemEvent(
                        "compactFailed",
                        message: message,
                        percentLeft: beforeThreshold.PercentLeft,
                        tokenCount: beforeThreshold.Tokens,
                        contextUsage: beforeUsage);
                    return await FinishAsync(new ThreadCompactResult
                    {
                        Outcome = "failed",
                        Message = message,
                        ContextUsage = beforeUsage
                    });
                }

                broker.PublishSystemEvent(
                    "compacting",
                    percentLeft: beforeThreshold.PercentLeft,
                    tokenCount: beforeThreshold.Tokens);

                CompactionExecutionResult compactExecution;
                CompactionStatus status;
                var installedProviderNative = false;
                IReadOnlyList<ChatMessage>? pendingNeutralReplacement = null;
                try
                {
                    var manualCoveredTurn = thread.Turns
                        .Where(candidate =>
                            candidate.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled)
                        .OrderBy(candidate => candidate.StartedAt)
                        .ThenBy(candidate => candidate.Id, StringComparer.Ordinal)
                        .LastOrDefault();
                    using var codexResponsesScope = OpenAIResponsesCodexRuntimeScope.Set(
                        new OpenAIResponsesCodexRuntimeContext(
                            ThreadConversationIdentity.Create(
                                thread,
                                turn: null,
                                owner.GetOrCreateCodexContextWindow(threadId).CurrentWindowId,
                                ThreadConversationRequestKind.Compaction)));
                    var providerHistory = await owner.CreateResponsesProviderHistoryContextAsync(
                        thread,
                        turn: null,
                        session,
                        maintenanceCt,
                        manualCoveredTurn?.Id);
                    using var providerHistoryScope = providerHistory == null
                        ? null
                        : OpenAIResponsesProviderHistoryRuntimeScope.Set(
                            providerHistory,
                            thread.Ephemeral
                                ? context =>
                                {
                                    if (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime))
                                        runtime.ResponsesProviderHistorySnapshot = context.CaptureSnapshot();
                                }
                                : null);
                    var agentOptions = agent.ChatOptions ?? new ChatOptions();
                    var manualOptions = manualPromptSnapshot is null
                        ? agentOptions
                        : MaintenanceForkRunner.BuildOptions(manualPromptSnapshot);
                    manualOptions.RawRepresentationFactory ??= agentOptions.RawRepresentationFactory;
                    manualOptions.AdditionalProperties ??= agentOptions.AdditionalProperties;
                    if (manualOptions.Tools is not { Count: > 0 } && fallbackTools is { Count: > 0 })
                        manualOptions.Tools = fallbackTools.ToList();
                    var currentConfig = owner._appConfigMonitor?.Current
                        ?? owner.AgentFactory.RuntimeContext.Config;
                    var providerRuntime = owner.AgentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
                        currentConfig,
                        thread.Configuration?.ProviderId,
                        thread.Configuration?.Model);
                    manualOptions = FastModeChatClient.PrepareOptions(
                        manualOptions,
                        currentConfig,
                        providerRuntime.Protocol,
                        providerRuntime.Model,
                        thread.Configuration?.Speed ?? InferenceSpeed.Standard) ?? manualOptions;
                    compactExecution = await coordinator.ExecuteAsync(
                        new CompactionExecutionRequest(
                            CompactionTrigger.Manual,
                            CompactionPhase.Manual,
                            historyForEstimate,
                            threadId,
                            before,
                            thread.LastActiveAt,
                            manualPromptSnapshot,
                            fallbackTools,
                            CarryRequestOverhead: false,
                            Options: manualOptions,
                            ProviderBridge: providerHistory),
                        maintenanceCt);
                    status = compactExecution.Status;
                    if (status.Success)
                    {
                        if (compactExecution.Replacement is CompactionReplacement.Neutral neutralReplacement)
                        {
                            pendingNeutralReplacement = neutralReplacement.Messages
                                .Select(message => message.Clone())
                                .ToList();
                        }
                        else if (compactExecution.Replacement is CompactionReplacement.ProviderNative nativeReplacement
                                 && providerHistory != null)
                        {
                            await coordinator.InstallProviderNativeAsync(
                                threadId,
                                compactExecution.BackendId,
                                providerHistory,
                                nativeReplacement,
                                maintenanceCt);
                            installedProviderNative = true;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Compaction backend '{compactExecution.BackendId}' returned no installable replacement.");
                        }
                        owner.InvalidatePromptRequestSnapshot(threadId, "manual_compaction");
                    }
                }
                catch (OperationCanceledException)
                {
                    broker.PublishSystemEvent(
                        "compactCancelled",
                        message: "cancelled",
                        percentLeft: beforeThreshold.PercentLeft,
                        tokenCount: beforeThreshold.Tokens,
                        contextUsage: beforeUsage);
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
                        tokenCount: beforeThreshold.Tokens,
                        contextUsage: beforeUsage);
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
                        var coveredTurn = thread.Turns
                            .Where(t => t.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled)
                            .OrderBy(t => t.StartedAt)
                            .ThenBy(t => t.Id, StringComparer.Ordinal)
                            .LastOrDefault();
                        if (!installedProviderNative && pendingNeutralReplacement != null)
                        {
                            if (coveredTurn == null)
                            {
                                throw new InvalidOperationException(
                                    "Cannot commit a neutral compaction replacement without a terminal Turn boundary.");
                            }
                            await TryAppendCompactionCheckpointAsync(
                                threadId,
                                coveredTurn.Id,
                                pendingNeutralReplacement,
                                new PendingCompactionCheckpoint(
                                    "manual",
                                    CompactionOutcomeToWire(status.Outcome),
                                    status.ThresholdBefore.Tokens,
                                    status.ThresholdAfter.Tokens),
                                maintenanceCt);
                            session.Clear();
                            session.AddRange(pendingNeutralReplacement);
                        }

                        var contextUsage = await owner.SaveReplacementContextUsageSnapshotAsync(
                            threadId,
                            status.ThresholdAfter.Tokens,
                            source: installedProviderNative
                                ? "provider_compacted_estimate"
                                : "compacted_estimate",
                            ct: maintenanceCt);
                        if (!installedProviderNative)
                        {
                            owner.TryAdvanceCodexContextWindowAfterReplacement(threadId);
                            await owner.TryReplaceResponsesProviderHistoryAsync(
                                thread,
                                session,
                                "manual_compaction",
                                maintenanceCt);
                        }
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

                        await owner.RunCompactionHookAsync(
                            HookEvent.PostCompact,
                            thread,
                            coveredTurnId,
                            "manual",
                            status.ThresholdBefore,
                            status.ThresholdAfter,
                            contextUsage,
                            CompactionOutcomeToWire(status.Outcome),
                            maintenanceCt);

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
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: beforeUsage);
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
                            tokenCount: status.ThresholdAfter.Tokens,
                            contextUsage: beforeUsage);
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
                var session = await owner.Persistence.LoadModelHistoryAsync(threadId, ct);
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
                using var codexResponsesScope = OpenAIResponsesCodexRuntimeScope.Set(
                    new OpenAIResponsesCodexRuntimeContext(
                        ThreadConversationIdentity.Create(
                            thread,
                            turn: null,
                            owner.GetOrCreateCodexContextWindow(threadId).CurrentWindowId,
                            ThreadConversationRequestKind.Memory)));
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
                owner._appConfigMonitor?.Current ?? owner.AgentFactory.RuntimeContext.Config,
                thread?.Configuration?.ContextWindow?.Mode ?? ContextWindowMode.Default);

        public CompactionCoordinator GetCompactionCoordinatorForThread(string threadId)
        {
            owner._runtimeRegistry.TryGetThread(threadId, out var thread);
            return GetCompactionCoordinatorForThread(threadId, thread);
        }

        public CompactionCoordinator GetCompactionCoordinatorForThread(SessionThread thread) =>
            GetCompactionCoordinatorForThread(thread.Id, thread);

        public CompactionCoordinator GetCompactionCoordinatorForThread(
            string threadId,
            SessionThread? thread)
        {
            var pipeline = GetCompactionPipelineForThread(threadId, thread);
            if (thread is null)
            {
                return new CompactionCoordinator(pipeline);
            }

            var config = owner._appConfigMonitor?.Current ?? owner.AgentFactory.RuntimeContext.Config;
            var runtime = owner.AgentFactory.RuntimeContext.ChatClientRegistry.ResolveMainRuntime(
                config,
                thread.Configuration?.ProviderId,
                thread.Configuration?.Model);
            if (!ChatGptResponsesCompactEligibility.IsEligible(
                    runtime,
                    thread.HistoryMode,
                    thread.ProviderHistorySchemaVersion))
                return new CompactionCoordinator(pipeline);

            return new CompactionCoordinator(
                pipeline,
                _ => new ChatGptResponsesCompactBackend(
                    runtime.Model,
                    owner.AgentFactory.RuntimeContext.ChatClientRegistry
                        .GetChatGptResponsesCompactTransport(runtime),
                    pipeline.EvaluateThreshold,
                    owner.AgentFactory.RuntimeContext.ChatClientRegistry.GetChatClient(runtime)),
                pipeline.GetBackendFailureTracker);
        }

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
            List<ChatMessage> session,
            SessionEventChannel eventChannel,
            Func<int> nextItemSequence)
        {
            if (ThreadVisibility.IsInternal(thread))
                return false;

            var memoryConfig = owner._appConfigMonitor?.Current.Memory
                ?? owner.AgentFactory.RuntimeContext.Config.Memory;

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
            IReadOnlyList<ChatMessage> replacementHistory,
            PendingCompactionCheckpoint checkpoint,
            CancellationToken ct)
        {
            if (owner.IsPendingPermanentDeletion(threadId))
                return;

            var history = replacementHistory.Select(message => message.Clone()).ToList();

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
                throw;
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
            await owner.EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);
            if (owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
                && runtime.LatestToolSnapshot is { } snapshot)
            {
                return AgentFactory.ProjectSnapshotTools(snapshot);
            }

            throw new InvalidOperationException(
                $"Thread '{thread.Id}' does not have an effective tool snapshot for compaction.");
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
            List<ChatMessage> session,
            SessionThread thread)
        {
            if (session.Count > 0)
                return session.ToList();

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
