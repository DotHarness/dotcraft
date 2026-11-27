using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class TurnUsageAccounting(
        SessionService owner,
        SessionThread thread,
        SessionTurn turn,
        SessionEventChannel eventChannel,
        TraceCollector? traceCollector)
    {
        private readonly TokenUsageRequestAccumulator _accumulator = new();
        private readonly TurnCostTracker _cost = new();
        private int _traceBaseline;
        private int? _requestIndex;
        private ContextUsageSnapshot? _explicitContextUsage;
        private TokenUsageInfo _requestUsage = new();

        public TokenUsageInfo MainUsage { get; private set; } = new();

        public void CaptureTraceBaseline() =>
            _traceBaseline = traceCollector?.GetTokenUsageCount(thread.Id) ?? 0;

        public void ObserveResponse(ChatResponseUpdate update)
        {
            if (TokenUsageRequestMetadata.TryGetRequestIndex(update) is { } requestIndex && requestIndex != _requestIndex)
            {
                _requestIndex = requestIndex;
                _explicitContextUsage = null;
            }

            _cost.ObserveUpdate(update, _requestIndex);
        }

        public async Task<TokenUsageInfo?> RecordAsync(
            UsageContent usage,
            TokenTracker tracker,
            IReadOnlyList<ChatMessage> history,
            CompactionPipeline? pipeline)
        {
            var hasExplicitContext = ProviderContextUsage.IsAttached(usage);
            var measurement = ProviderContextUsage.TryGet(usage);
            var contextChanged = hasExplicitContext && (measurement is not null || _explicitContextUsage is null);
            if (contextChanged)
                _explicitContextUsage = await owner.SaveExplicitContextUsageAsync(
                    thread.Id, tracker, measurement, history, pipeline);

            var snapshot = TokenUsageExtractor.FromUsageContent(usage);
            var usageDelta = snapshot.InputTokens > 0 || snapshot.OutputTokens > 0
                ? _accumulator.ApplySnapshot(snapshot, _requestIndex)
                : default;
            var delta = usageDelta.Usage;
            var billed = delta.InputTokens > 0 || delta.OutputTokens > 0
                || delta.CachedInputTokens > 0 || delta.CacheWriteInputTokens > 0
                || delta.ReasoningOutputTokens > 0;
            if (!billed && !contextChanged)
                return null;

            var contextUsage = _explicitContextUsage;
            var usesExplicitContext = contextUsage is not null;
            TokenUsageInfo? requestDelta = null;
            if (billed)
            {
                requestDelta = new TokenUsageInfo
                {
                    InputTokens = delta.InputTokens,
                    OutputTokens = delta.OutputTokens,
                    CachedInputTokens = delta.CachedInputTokens,
                    CacheWriteInputTokens = delta.CacheWriteInputTokens,
                    ReasoningOutputTokens = delta.ReasoningOutputTokens,
                    LlmCallCount = usageDelta.LlmCallDelta,
                    TotalTokens = delta.TotalTokens
                };
                _cost.AddUsage(delta, usageDelta.IsNewRequest, _requestIndex);
                MainUsage += requestDelta;
                _requestUsage = usageDelta.IsNewRequest
                    ? requestDelta
                    : _requestUsage + requestDelta;
                if (usesExplicitContext)
                {
                    tracker.RecordStreamingUsage(delta.InputTokens, delta.OutputTokens, delta.CachedInputTokens,
                        delta.CacheWriteInputTokens, delta.ReasoningOutputTokens);
                }
                else
                {
                    tracker.UpdateWithStreamingDeltas(delta.InputTokens, delta.OutputTokens, delta.CachedInputTokens,
                        delta.CacheWriteInputTokens, delta.ReasoningOutputTokens, snapshot.InputTokens, snapshot.OutputTokens);
                    var anchor = owner.UpdateContextUsageAnchor(thread.Id, tracker.LastInputTokens);
                    contextUsage = await owner.SaveContextUsageSnapshotAsync(
                        thread.Id, tracker.LastContextTokens, anchor, "provider_context", isEstimate: false,
                        pipeline: pipeline);
                }
            }

            var contextInput = usesExplicitContext && tracker.LastInputTokens == 0
                ? (long?)null : tracker.LastInputTokens;
            eventChannel.EmitUsageDelta(
                delta.InputTokens,
                delta.OutputTokens,
                cachedInputTokens: delta.CachedInputTokens,
                cacheWriteInputTokens: delta.CacheWriteInputTokens,
                reasoningOutputTokens: delta.ReasoningOutputTokens,
                llmCallDelta: usageDelta.LlmCallDelta,
                totalInputTokens: contextInput,
                totalOutputTokens: MainUsage.OutputTokens,
                contextInputTokens: contextInput,
                turnInputTokens: MainUsage.InputTokens,
                turnOutputTokens: MainUsage.OutputTokens,
                turnLlmCalls: MainUsage.LlmCallCount,
                contextUsage: contextUsage,
                requestTiming: billed && TokenUsageRequestTiming.TryGet(usage) is { } timing
                    ? new UsageRequestTiming(timing.FirstOutputLatencyMs, timing.GenerationMs)
                    : null);
            if (billed)
            {
                turn.TokenUsage = WithSubAgentUsage(tracker, MainUsage.LlmCallCount);
                owner.PublishThreadTokenUsage(
                    thread, turn, turn.TokenUsage, _requestUsage,
                    contextUsage?.ContextWindow is > 0 ? contextUsage.ContextWindow : null);
            }

            return requestDelta;
        }

        public TokenUsageInfo CompleteUsage(TokenTracker tracker)
        {
            var tracedCalls = Math.Max(0,
                (traceCollector?.GetTokenUsageCount(thread.Id) ?? _traceBaseline) - _traceBaseline);
            return WithSubAgentUsage(tracker, Math.Max(MainUsage.LlmCallCount, tracedCalls));
        }

        public void RecordCompletedUsage() => owner.RecordTurnTokenUsage(thread, turn, _cost);

        private TokenUsageInfo WithSubAgentUsage(TokenTracker tracker, int mainCalls)
        {
            var input = MainUsage.InputTokens + tracker.SubAgentInputTokens;
            var output = MainUsage.OutputTokens + tracker.SubAgentOutputTokens;
            return new TokenUsageInfo
            {
                InputTokens = input,
                OutputTokens = output,
                CachedInputTokens = Math.Clamp(MainUsage.CachedInputTokens + tracker.SubAgentCachedInputTokens, 0, input),
                CacheWriteInputTokens = Math.Clamp(MainUsage.CacheWriteInputTokens + tracker.SubAgentCacheWriteInputTokens, 0, input),
                ReasoningOutputTokens = MainUsage.ReasoningOutputTokens + tracker.SubAgentReasoningOutputTokens,
                LlmCallCount = mainCalls + tracker.SubAgentLlmCallCount,
                TotalTokens = input + output
            };
        }
    }
}
