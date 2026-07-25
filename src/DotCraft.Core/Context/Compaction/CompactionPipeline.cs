using System.Globalization;
using DotCraft.Configuration;
using DotCraft.Tracing;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Outcome of a single compaction attempt.
/// </summary>
public enum CompactionOutcome
{
    /// <summary>No action was needed (below thresholds or nothing to summarize).</summary>
    Skipped,
    /// <summary>The microcompact pass cleared enough content to stay below the auto threshold.</summary>
    Micro,
    /// <summary>A partial summary was produced and the prefix was replaced in-place.</summary>
    Partial,
    /// <summary>The attempt failed (LLM error, cancellation, circuit breaker tripped).</summary>
    Failed,
}

/// <summary>
/// Rich status record returned by pipeline entry points for logging / eventing.
/// </summary>
public sealed record CompactionStatus(
    CompactionOutcome Outcome,
    int EstimatedTokensBefore,
    int EstimatedTokensAfter,
    CompactionThreshold ThresholdBefore,
    CompactionThreshold ThresholdAfter,
    int ClearedToolResults = 0,
    string? FailureReason = null)
{
    public bool Success => Outcome is CompactionOutcome.Micro or CompactionOutcome.Partial;
}

/// <summary>
/// Result of a history-level compaction attempt. The returned message list is
/// the model-visible history that callers should use for the next sampling
/// request when the status succeeded.
/// </summary>
public sealed record CompactionHistoryResult(
    CompactionStatus Status,
    IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// Threshold evaluation for a given token count.
/// </summary>
public sealed record CompactionThreshold(
    long Tokens,
    int AutoThreshold,
    int WarningThreshold,
    int ErrorThreshold,
    int BlockingLimit,
    double PercentLeft)
{
    public bool AboveWarning => Tokens >= WarningThreshold;
    public bool AboveError => Tokens >= ErrorThreshold;
    public bool AboveAuto => Tokens >= AutoThreshold;
    public bool AboveBlocking => Tokens >= BlockingLimit;
}

/// <summary>
/// Orchestrator for the layered compaction pipeline. Owns the microcompactor,
/// partial compactor, failure tracker, and the token-based threshold
/// evaluation. Emits <see cref="CompactionStatus"/> values that
/// <see cref="Protocol.SessionService"/> translates into lifecycle events.
/// </summary>
public sealed class CompactionPipeline
{
    private readonly CompactionConfig _config;
    private readonly MicroCompactor _micro;
    private readonly PartialCompactor _partial;
    private readonly FullCompactor _full;
    private readonly CompactionFailureTracker _failures;
    private readonly MaintenanceForkRunner _maintenanceForkRunner;
    private readonly MaintenanceForkCacheOptions? _cacheOptions;

    public CompactionPipeline(
        CompactionConfig config,
        IChatClient summaryChatClient,
        TraceCollector? traceCollector = null,
        MaintenanceForkCacheOptions? cacheOptions = null)
    {
        _config = config;
        _micro = new MicroCompactor(config);
        _cacheOptions = cacheOptions;
        _maintenanceForkRunner = new MaintenanceForkRunner(
            summaryChatClient,
            traceCollector,
            cacheOptions);
        _partial = new PartialCompactor(summaryChatClient, config, _maintenanceForkRunner, traceCollector);
        _full = new FullCompactor(summaryChatClient, _maintenanceForkRunner, traceCollector, config);
        _failures = new CompactionFailureTracker(config.MaxConsecutiveFailures);
    }

    /// <summary>
    /// Exposed for unit tests that need to poke the breaker directly.
    /// </summary>
    internal CompactionFailureTracker Failures => _failures;

    /// <summary>
    /// Exposes the configured effective context window. Used by the wire layer
    /// to populate <see cref="Protocol.ContextUsageSnapshot.ContextWindow"/>.
    /// </summary>
    public int EffectiveContextWindow => _config.EffectiveContextWindow();

    /// <summary>
    /// Computes the <see cref="CompactionThreshold"/> for a given token count.
    /// </summary>
    public CompactionThreshold EvaluateThreshold(long tokens)
    {
        var effective = _config.EffectiveContextWindow();
        var autoThreshold = _config.AutoCompactThreshold();
        var warning = _config.WarningThreshold();
        var error = _config.ErrorThreshold();
        var blocking = _config.BlockingLimit();

        var percentLeft = effective <= 0
            ? 0.0
            : Math.Max(0.0, 1.0 - (tokens / (double)effective));

        return new CompactionThreshold(
            tokens,
            autoThreshold,
            warning,
            error,
            blocking,
            percentLeft);
    }

    /// <summary>
    /// Evaluates whether auto-compact should run for the given session.
    /// Primary token source is <paramref name="inputTokenHint"/> (typically
    /// <c>tokenTracker.LastInputTokens</c>); when that is zero or negative,
    /// falls back to <see cref="MessageTokenEstimator.Estimate"/>.
    /// </summary>
    public async Task<CompactionStatus> TryAutoCompactAsync(
        List<ChatMessage> session,
        string threadId,
        long inputTokenHint,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        PromptRequestSnapshot? snapshot = null)
    {
        int BeforeFromHintOrZero() => inputTokenHint > 0
            ? (int)Math.Min(int.MaxValue, inputTokenHint)
            : 0;

        if (!_config.AutoCompactEnabled)
        {
            var skippedBefore = BeforeFromHintOrZero();
            var threshold = EvaluateThreshold(skippedBefore);
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                skippedBefore, skippedBefore,
                threshold, threshold,
                FailureReason: "auto_disabled");
        }

        if (_failures.IsTripped(threadId))
        {
            var skippedBefore = BeforeFromHintOrZero();
            var threshold = EvaluateThreshold(skippedBefore);
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                skippedBefore, skippedBefore,
                threshold, threshold,
                FailureReason: "circuit_breaker_tripped");
        }

        var history = SnapshotHistory(session);
        var before = inputTokenHint > 0
            ? (int)Math.Min(int.MaxValue, inputTokenHint)
            : MessageTokenEstimator.Estimate(history);
        var requestOverheadTokens = EstimateRequestOverheadTokens(history, inputTokenHint);
        var beforeThreshold = EvaluateThreshold(before);

        if (!beforeThreshold.AboveAuto)
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                before, before, beforeThreshold, beforeThreshold);

        var result = await RunCompactionAsync(
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            snapshot: snapshot,
            requestOverheadTokens: requestOverheadTokens);

        ApplyHistoryReplacement(session, result.Messages);
        return result.Status;
    }

    /// <summary>
    /// Auto compacts an explicit model-visible history snapshot. This is used
    /// at sampling boundaries where the next request already has its complete
    /// message list, including tool results or same-turn guidance.
    /// </summary>
    public async Task<CompactionHistoryResult> TryAutoCompactHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        string threadId,
        long inputTokenHint,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        PromptRequestSnapshot? snapshot = null)
    {
        var before = inputTokenHint > 0
            ? (int)Math.Min(int.MaxValue, inputTokenHint)
            : MessageTokenEstimator.Estimate(history);
        var requestOverheadTokens = EstimateRequestOverheadTokens(history, inputTokenHint);
        var beforeThreshold = EvaluateThreshold(before);

        if (!_config.AutoCompactEnabled)
        {
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Skipped,
                    before, before,
                    beforeThreshold, beforeThreshold,
                    FailureReason: "auto_disabled"),
                history);
        }

        if (_failures.IsTripped(threadId))
        {
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Skipped,
                    before, before,
                    beforeThreshold, beforeThreshold,
                    FailureReason: "circuit_breaker_tripped"),
                history);
        }

        if (!beforeThreshold.AboveAuto)
        {
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Skipped,
                    before, before,
                    beforeThreshold, beforeThreshold),
                history);
        }

        return await RunCompactionAsync(
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            snapshot: snapshot,
            requestOverheadTokens: requestOverheadTokens);
    }

    /// <summary>
    /// Reactive compaction: called from the mid-turn error path when the
    /// model rejected the request for being too long. Skips threshold
    /// evaluation and always runs summary-producing compaction.
    /// </summary>
    public async Task<CompactionStatus> TryReactiveCompactAsync(
        List<ChatMessage> session,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (!_config.ReactiveCompactEnabled)
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "reactive_disabled");

        if (_failures.IsTripped(threadId))
            return new CompactionStatus(
                CompactionOutcome.Failed,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "circuit_breaker_tripped");

        var history = SnapshotHistory(session);
        var before = MessageTokenEstimator.Estimate(history);
        var beforeThreshold = EvaluateThreshold(before);

        var result = await RunCompactionAsync(
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            forcePartial: true);
        ApplyHistoryReplacement(session, result.Messages);
        return result.Status;
    }

    /// <summary>
    /// Manual compaction (slash command, dashboard button). Skips the auto
    /// threshold check but still respects the circuit breaker and blocking
    /// limit.
    /// </summary>
    public async Task<CompactionStatus> TryManualCompactAsync(
        List<ChatMessage> session,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        long? inputTokenHint = null,
        PromptRequestSnapshot? snapshot = null,
        IReadOnlyList<AITool>? fallbackTools = null,
        bool carryRequestOverhead = true)
    {
        if (_failures.IsTripped(threadId))
            return new CompactionStatus(
                CompactionOutcome.Failed,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "circuit_breaker_tripped");

        var history = SnapshotHistory(session);
        var result = await TryManualCompactHistoryAsync(
            history,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            inputTokenHint,
            snapshot,
            fallbackTools,
            carryRequestOverhead);
        ApplyHistoryReplacement(session, result.Messages);
        return result.Status;
    }

    /// <summary>
    /// Manual compaction for an explicit history snapshot. This is used by
    /// clients that need to compact an idle persisted session even when the
    /// loaded model history is represented directly by MEAI messages.
    /// </summary>
    public async Task<CompactionHistoryResult> TryManualCompactHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        long? inputTokenHint = null,
        PromptRequestSnapshot? snapshot = null,
        IReadOnlyList<AITool>? fallbackTools = null,
        bool carryRequestOverhead = true)
    {
        var before = inputTokenHint is > 0
            ? (int)Math.Min(int.MaxValue, inputTokenHint.Value)
            : MessageTokenEstimator.Estimate(history);
        var requestOverheadTokens = carryRequestOverhead
            ? EstimateRequestOverheadTokens(history, inputTokenHint ?? 0)
            : 0;
        var beforeThreshold = EvaluateThreshold(before);

        if (history.Count == 0)
        {
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Skipped,
                    before, before,
                    beforeThreshold, beforeThreshold,
                    FailureReason: "empty_history"),
                history);
        }

        if (_failures.IsTripped(threadId))
        {
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    before, before,
                    beforeThreshold, beforeThreshold,
                    FailureReason: "circuit_breaker_tripped"),
                history);
        }

        var partial = await RunCompactionAsync(
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            snapshot: snapshot,
            fallbackTools: fallbackTools,
            forcePartial: true,
            requestOverheadTokens: requestOverheadTokens);
        if (partial.Status.Success)
            return partial;

        return await RunFullCompactionAsync(
            history,
            before,
            beforeThreshold,
            threadId,
            cancellationToken,
            snapshot,
            fallbackTools,
            requestOverheadTokens);
    }

    /// <summary>
    /// Drops any per-thread state (called on thread deletion / clear).
    /// </summary>
    public void Forget(string threadId) => _failures.Forget(threadId);

    private async Task<CompactionHistoryResult> RunCompactionAsync(
        IReadOnlyList<ChatMessage> history,
        int before,
        CompactionThreshold beforeThreshold,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        PromptRequestSnapshot? snapshot = null,
        IReadOnlyList<AITool>? fallbackTools = null,
        bool forcePartial = false,
        int requestOverheadTokens = 0)
    {
        var microResult = !forcePartial && ShouldRunColdCacheMicro(lastAssistantTimestampUtc)
            ? _micro.Run(history, lastAssistantTimestampUtc)
            : new MicroCompactResult(history, 0, 0, MicroCompactTrigger.None);
        var afterMicroHistory = microResult.Messages;
        var afterMicroTokens = AddRequestOverhead(
            MessageTokenEstimator.Estimate(afterMicroHistory),
            requestOverheadTokens);
        var afterMicroThreshold = EvaluateThreshold(afterMicroTokens);

        if (microResult.Trigger != MicroCompactTrigger.None && !forcePartial)
        {
            if (!afterMicroThreshold.AboveAuto)
            {
                _failures.RecordSuccess(threadId);
                return new CompactionHistoryResult(
                    new CompactionStatus(
                        CompactionOutcome.Micro,
                        before,
                        afterMicroTokens,
                        beforeThreshold,
                        afterMicroThreshold,
                        microResult.ClearedCount),
                    afterMicroHistory);
            }
        }

        var historyForPartial = microResult.Trigger != MicroCompactTrigger.None
            ? afterMicroHistory
            : history;

        PartialCompactAttempt partial;
        try
        {
            partial = await _partial.CompactAsync(
                historyForPartial,
                snapshot,
                threadId,
                fallbackTools,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _failures.RecordFailure(threadId);
            var afterFailure = AddRequestOverhead(
                MessageTokenEstimator.Estimate(historyForPartial),
                requestOverheadTokens);
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    before,
                    afterFailure,
                    beforeThreshold,
                    EvaluateThreshold(afterFailure),
                    microResult.ClearedCount,
                    FailureReason: ex.Message),
                historyForPartial);
        }

        if (partial.Result is null)
        {
            var afterFailure = AddRequestOverhead(
                MessageTokenEstimator.Estimate(historyForPartial),
                requestOverheadTokens);
            if (partial.Reason is "empty_history" or "no_summarizable_prefix")
            {
                return new CompactionHistoryResult(
                    new CompactionStatus(
                        CompactionOutcome.Skipped,
                        before,
                        afterFailure,
                        beforeThreshold,
                        EvaluateThreshold(afterFailure),
                        microResult.ClearedCount,
                        FailureReason: partial.Reason),
                    historyForPartial);
            }

            _failures.RecordFailure(threadId);
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    before,
                    afterFailure,
                    beforeThreshold,
                    EvaluateThreshold(afterFailure),
                    microResult.ClearedCount,
                    FailureReason: partial.Reason ?? "summary_unavailable"),
                historyForPartial);
        }

        var partialResult = partial.Result;
        var summaryMessage = new ChatMessage(ChatRole.Assistant, partialResult.FormattedSummary);
        var newHistory = new List<ChatMessage>(1 + partialResult.PreservedTail.Count) { summaryMessage };
        newHistory.AddRange(ImageContentSanitizingChatClient.ReplaceToolImagesWithDescriptions(
            partialResult.PreservedTail));

        var afterTokens = AddRequestOverhead(
            MessageTokenEstimator.Estimate(newHistory),
            requestOverheadTokens);
        var afterThreshold = EvaluateThreshold(afterTokens);
        _failures.RecordSuccess(threadId);

        return new CompactionHistoryResult(
            new CompactionStatus(
                CompactionOutcome.Partial,
                before,
                afterTokens,
                beforeThreshold,
                afterThreshold,
                microResult.ClearedCount),
            newHistory);
    }

    private async Task<CompactionHistoryResult> RunFullCompactionAsync(
        IReadOnlyList<ChatMessage> history,
        int before,
        CompactionThreshold beforeThreshold,
        string threadId,
        CancellationToken cancellationToken,
        PromptRequestSnapshot? snapshot,
        IReadOnlyList<AITool>? fallbackTools,
        int requestOverheadTokens = 0)
    {
        FullCompactAttempt full;
        try
        {
            full = await _full.CompactAsync(history, snapshot, threadId, fallbackTools, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _failures.RecordFailure(threadId);
            var afterFailure = AddRequestOverhead(
                MessageTokenEstimator.Estimate(history),
                requestOverheadTokens);
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    before,
                    afterFailure,
                    beforeThreshold,
                    EvaluateThreshold(afterFailure),
                    FailureReason: ex.Message),
                history);
        }

        if (full.Result is null)
        {
            var afterFailure = AddRequestOverhead(
                MessageTokenEstimator.Estimate(history),
                requestOverheadTokens);
            _failures.RecordFailure(threadId);
            return new CompactionHistoryResult(
                new CompactionStatus(
                    CompactionOutcome.Failed,
                    before,
                    afterFailure,
                    beforeThreshold,
                    EvaluateThreshold(afterFailure),
                    FailureReason: full.Reason ?? "summary_unavailable"),
                history);
        }

        var result = full.Result;
        var newHistory = new List<ChatMessage>(1 + result.PreservedTail.Count)
        {
            new(ChatRole.Assistant, result.FormattedSummary)
        };
        newHistory.AddRange(ImageContentSanitizingChatClient.ReplaceToolImagesWithDescriptions(
            result.PreservedTail));

        var afterTokens = AddRequestOverhead(
            MessageTokenEstimator.Estimate(newHistory),
            requestOverheadTokens);
        var afterThreshold = EvaluateThreshold(afterTokens);
        _failures.RecordSuccess(threadId);

        return new CompactionHistoryResult(
            new CompactionStatus(
                CompactionOutcome.Partial,
                before,
                afterTokens,
                beforeThreshold,
                afterThreshold),
            newHistory);
    }

    private bool ShouldRunColdCacheMicro(DateTimeOffset? lastAssistantTimestampUtc)
    {
        if (!_config.MicrocompactEnabled ||
            _config.MicrocompactGapMinutes <= 0 ||
            lastAssistantTimestampUtc is not { } lastTime)
        {
            return false;
        }

        var protocol = _cacheOptions?.ProviderProtocol;
        if (_cacheOptions is not null && ModelProviderProtocols.IsOpenAIProtocol(protocol))
            return false;

        var effectiveGapMinutes = (double)Math.Max(0, _config.MicrocompactGapMinutes);
        if (IsAnthropicProtocol(protocol) &&
            _cacheOptions?.PromptCaching is { } promptCaching &&
            promptCaching.ShouldApply(_cacheOptions.Model))
        {
            effectiveGapMinutes = Math.Max(
                effectiveGapMinutes,
                ResolveAnthropicPromptCacheTtlMinutes(promptCaching.Ttl));
        }

        var gap = DateTimeOffset.UtcNow - lastTime;
        return gap >= TimeSpan.Zero && gap.TotalMinutes >= effectiveGapMinutes;
    }

    private static bool IsAnthropicProtocol(string? protocol)
    {
        try
        {
            return string.Equals(
                ModelProviderProtocols.Normalize(protocol),
                ModelProviderProtocols.Anthropic,
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static double ResolveAnthropicPromptCacheTtlMinutes(string? ttl)
    {
        if (string.IsNullOrWhiteSpace(ttl))
            return 5;

        var value = ttl.Trim().ToLowerInvariant();
        if (TryParseDuration(value, "h", out var hours))
            return hours * 60;
        if (TryParseDuration(value, "m", out var minutes))
            return minutes;
        if (TryParseDuration(value, "s", out var seconds))
            return seconds / 60;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawMinutes) &&
            rawMinutes > 0)
        {
            return rawMinutes;
        }

        return 5;
    }

    private static bool TryParseDuration(string value, string suffix, out double parsed)
    {
        parsed = 0;
        if (!value.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var number = value[..^suffix.Length].Trim();
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
            parsed > 0;
    }

    private static IReadOnlyList<ChatMessage> SnapshotHistory(List<ChatMessage> history) =>
        [.. history];

    private static int EstimateRequestOverheadTokens(
        IReadOnlyList<ChatMessage> history,
        long inputTokenHint)
    {
        if (inputTokenHint <= 0)
            return 0;

        var messageEstimate = MessageTokenEstimator.Estimate(history);
        var overhead = inputTokenHint - messageEstimate;
        return overhead <= 0
            ? 0
            : (int)Math.Min(int.MaxValue, overhead);
    }

    private static int AddRequestOverhead(int messageTokens, int overheadTokens)
    {
        if (overheadTokens <= 0)
            return messageTokens;

        return (int)Math.Min(int.MaxValue, (long)messageTokens + overheadTokens);
    }

    private static void ApplyHistoryReplacement(
        List<ChatMessage> history,
        IReadOnlyList<ChatMessage> newHistory)
    {
        history.Clear();
        history.AddRange(newHistory);
    }
}
