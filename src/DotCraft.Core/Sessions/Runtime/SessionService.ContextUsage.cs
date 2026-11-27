using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ContextUsageBreakdown = DotCraft.Sessions.Wire.ContextUsageBreakdown;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private const string ProviderOccupancySource = "provider_occupancy";

    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
        => ThreadAccess.TryGetContextUsageSnapshot(threadId);

    public ContextUsageBreakdown? TryGetContextUsageBreakdown(string threadId)
    {
        if (!_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            || runtime.ContextComposition is not { } composition
            || TryGetContextUsageSnapshot(threadId) is not { } usage)
        {
            return null;
        }

        var memoryStores = ResolveMemoryIndexStores(runtime.Thread.Configuration);
        IReadOnlyList<string> memoryIndexFiles = memoryStores is null
            ? []
            : [
                memoryStores.Own.IndexFilePath,
                .. memoryStores.Attached.Select(static store =>
                    Path.GetFullPath(Path.Combine(store.DirectoryPath, Memory.MemoryStore.IndexFileName)))
            ];
        return composition.ToBreakdown(usage, ResolveAgentInstructions(runtime.Thread).Sources, memoryIndexFiles);
    }

    /// <inheritdoc />
    public ThreadSummaryRuntime GetThreadRuntimeSnapshot(SessionThread thread)
        => ThreadAccess.GetRuntimeSnapshot(thread);

    internal PromptRequestSnapshot? TryGetLastPromptRequestSnapshot(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.LastPromptRequest
            : null;

    internal PromptRequestSnapshot? TryGetValidLastPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        bool invalidateOnMismatch = true)
    {
        return TryGetValidLastPromptRequestSnapshot(
            threadId,
            currentHistory,
            out _,
            invalidateOnMismatch);
    }

    internal PromptRequestSnapshot? TryGetValidLastPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        out string? invalidReason,
        bool invalidateOnMismatch = true)
    {
        invalidReason = null;
        var snapshot = TryGetLastPromptRequestSnapshot(threadId);
        if (snapshot is null)
        {
            invalidReason = "snapshot_absent";
            return null;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ThreadId)
            && !string.Equals(snapshot.ThreadId, threadId, StringComparison.Ordinal))
        {
            invalidReason = "thread_mismatch";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        if (snapshot.Messages.Count > currentHistory.Count)
        {
            invalidReason = "snapshot_longer_than_history";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        var currentPrefixFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(
            currentHistory,
            snapshot.Messages.Count);
        if (!string.Equals(snapshot.MessageFingerprint, currentPrefixFingerprint, StringComparison.Ordinal))
        {
            invalidReason = "history_prefix_mismatch";
            if (invalidateOnMismatch)
                InvalidatePromptRequestSnapshot(threadId, invalidReason);
            return null;
        }

        return snapshot;
    }

    internal PromptRequestSnapshot? TryPrepareManualPromptRequestSnapshot(
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        int? estimatedInputTokens)
    {
        var validSnapshot = TryGetValidLastPromptRequestSnapshot(
            threadId,
            currentHistory,
            out var invalidReason,
            invalidateOnMismatch: false);
        if (validSnapshot is not null)
        {
            return validSnapshot with
            {
                TurnId = null,
                EstimatedInputTokens = estimatedInputTokens ?? validSnapshot.EstimatedInputTokens,
                SnapshotSource = PromptRequestSnapshotSources.ManualValid,
                SnapshotInvalidReason = null
            };
        }

        var template = TryGetLastPromptRequestSnapshot(threadId);
        if (template is null)
            return null;

        if (!string.IsNullOrWhiteSpace(template.ThreadId)
            && !string.Equals(template.ThreadId, threadId, StringComparison.Ordinal))
        {
            return null;
        }

        return template with
        {
            ThreadId = threadId,
            TurnId = null,
            EstimatedInputTokens = estimatedInputTokens ?? template.EstimatedInputTokens,
            SnapshotSource = PromptRequestSnapshotSources.ManualInvalid,
            SnapshotInvalidReason = invalidReason
        };
    }

    private static PromptRequestSnapshot RebasePromptRequestSnapshotMessages(
        PromptRequestSnapshot template,
        IReadOnlyList<ChatMessage> currentHistory)
    {
        var capturedMessages = MessageGrouper
            .NormalizeFunctionCallArguments(currentHistory)
            .Select(message => message.Clone())
            .ToArray();

        return template with
        {
            Messages = capturedMessages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(capturedMessages, capturedMessages.Length)
        };
    }

    private void PublishSampledPrompt(
        string threadId,
        PromptRequestSnapshot? request,
        OpaqueProviderHistorySnapshot history)
    {
        if (request is not null && _runtimeRegistry.TryGetRuntime(threadId, out var runtime))
            runtime.LastPromptRequest = request with { ProviderHistory = history };
    }

    private void InvalidatePromptRequestSnapshot(string threadId, string reason)
    {
        if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            && runtime.LastPromptRequest != null)
        {
            runtime.LastPromptRequest = null;
            logger?.LogDebug("Invalidated prompt request snapshot for thread {ThreadId}: {Reason}", threadId, reason);
        }
    }

    private ContextUsageAnchor? TryGetInMemoryContextUsageAnchor(string threadId) =>
        _runtimeRegistry.TryGetRuntime(threadId, out var runtime)
            ? runtime.ContextUsageAnchor
            : null;

    private ContextUsageSnapshot CreateContextUsageSnapshot(
        string threadId,
        long tokens,
        string? source = null,
        bool isEstimate = false,
        CompactionPipeline? pipeline = null)
    {
        pipeline ??= GetCompactionPipelineForThread(threadId);
        var threshold = pipeline.EvaluateThreshold(tokens);

        return new ContextUsageSnapshot
        {
            Tokens = threshold.Tokens,
            ContextWindow = pipeline.EffectiveContextWindow,
            AutoCompactThreshold = threshold.AutoThreshold,
            WarningThreshold = threshold.WarningThreshold,
            ErrorThreshold = threshold.ErrorThreshold,
            PercentLeft = threshold.PercentLeft,
            Source = source,
            IsEstimate = isEstimate
        };
    }

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor: null,
            source: null,
            isEstimate: false,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        ContextUsageAnchor? anchor,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor,
            source: null,
            isEstimate: false,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        string? source,
        bool isEstimate,
        CancellationToken ct = default)
        => await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor: null,
            source: source,
            isEstimate: isEstimate,
            ct: ct);

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        ContextUsageAnchor? anchor,
        string? source,
        bool isEstimate,
        CancellationToken ct = default,
        CompactionPipeline? pipeline = null)
    {
        var normalizedTokens = Math.Max(0, tokens);
        if (_runtimeRegistry.TryGetRuntime(threadId, out var ephemeralRuntime) && ephemeralRuntime.Thread.Ephemeral)
        {
            ephemeralRuntime.ContextUsageAnchor = anchor;
            return CreateContextUsageSnapshot(threadId, normalizedTokens, source, isEstimate, pipeline);
        }
        if (anchor is not null)
        {
            var normalizedAnchor = anchor with { Tokens = Math.Max(0, anchor.Tokens) };
            await persistence.SaveContextUsageAnchorAsync(
                threadId,
                normalizedTokens,
                normalizedAnchor,
                source,
                isEstimate,
                ct);
            if (_runtimeRegistry.TryGetRuntime(threadId, out var runtime))
                runtime.ContextUsageAnchor = normalizedAnchor;
        }
        else
        {
            await persistence.SaveContextUsageTokensAsync(threadId, normalizedTokens, source, isEstimate, ct);
        }

        return CreateContextUsageSnapshot(threadId, normalizedTokens, source, isEstimate, pipeline);
    }

    private async Task<ContextUsageSnapshot> SaveReplacementContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        string source,
        CancellationToken ct = default,
        CompactionPipeline? pipeline = null)
    {
        var snapshot = await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor: null,
            source,
            isEstimate: true,
            ct,
            pipeline);
        ClearContextUsageAnchor(threadId);
        return snapshot;
    }

    private Task SavePreparedContextEstimateAsync(
        string threadId,
        ContextTokenUsageEstimate estimate,
        CancellationToken ct,
        CompactionPipeline? pipeline)
    {
        if (estimate.PreserveMeasuredDisplay)
            return Task.CompletedTask;

        var compacted = string.Equals(estimate.Source, "provider_compacted_estimate", StringComparison.Ordinal);
        var anchor = compacted ? null : TryGetInMemoryContextUsageAnchor(threadId)
            ?? persistence.LoadContextUsageAnchor(threadId);
        return SaveContextUsageSnapshotAsync(
            threadId,
            estimate.Tokens,
            anchor,
            source: compacted ? estimate.Source : "estimate",
            isEstimate: true,
            ct,
            pipeline);
    }

    private async Task<ContextUsageSnapshot> SaveExplicitContextUsageAsync(
        string threadId,
        TokenTracker tracker,
        ProviderContextUsage? measurement,
        IReadOnlyList<ChatMessage> history,
        CompactionPipeline? pipeline)
    {
        if (measurement is { OccupiedTokens: >= 0 } context)
        {
            tracker.SetContextUsage(context.InputTokens, context.OccupiedTokens);
            var anchor = context.InputTokens is { } inputTokens
                ? UpdateContextUsageAnchor(threadId, inputTokens, ContextUsageAnchorBoundary.ProviderMeasurement)
                : null;
            if (anchor is null)
                ClearContextUsageAnchor(threadId);
            var source = context.InputTokens is null ? ProviderOccupancySource : "provider_context";
            return await SaveContextUsageSnapshotAsync(
                threadId, context.OccupiedTokens, anchor, source, isEstimate: false, pipeline: pipeline);
        }

        var request = TryGetLastPromptRequestSnapshot(threadId);
        var visibleHistory = request?.Messages ?? PrepareProviderVisibleHistory(history);
        var measuredAnchor = TryGetInMemoryContextUsageAnchor(threadId) ?? persistence.LoadContextUsageAnchor(threadId);
        var anchored = measuredAnchor?.BoundaryKind == ContextUsageAnchorBoundary.ProviderMeasurement
            ? ContextUsageTokenCounter.EstimateFromAnchorDetailed(
                measuredAnchor, visibleHistory, request?.RequestFingerprint, requireRequestFingerprint: true,
                request?.ContextUsageFingerprint, request?.BaseInstructionsTokenEstimate)
            : null;
        if (anchored is null)
        {
            measuredAnchor = null;
            ClearContextUsageAnchor(threadId);
        }

        tracker.SetContextUsage(null, 0);
        var estimate = anchored?.Tokens
            ?? (long)MessageTokenEstimator.Estimate(visibleHistory) + (request?.EstimateRequestOverheadTokens() ?? 0);
        return await SaveContextUsageSnapshotAsync(
            threadId, estimate, measuredAnchor, "estimate", isEstimate: true, pipeline: pipeline);
    }

    private ContextUsageAnchor? UpdateContextUsageAnchor(
        string threadId, long anchorTokens, string boundary = ContextUsageAnchorBoundary.Request)
    {
        var snapshot = TryGetLastPromptRequestSnapshot(threadId);
        var anchorMessageCount = snapshot?.Messages.Count;
        if (snapshot is null || anchorMessageCount is not > 0)
            return null;

        var normalizedTokens = Math.Max(0, anchorTokens);
        var fingerprint = MessageTokenEstimator.ComputePrefixFingerprint(
            snapshot.Messages,
            anchorMessageCount.Value);
        var runtime = _runtimeRegistry.TryGetRuntime(threadId, out var existingRuntime)
            ? existingRuntime
            : null;
        var anchor = new ContextUsageAnchor(
            normalizedTokens,
            anchorMessageCount.Value,
            fingerprint,
            snapshot.RequestFingerprint,
            snapshot.ContextUsageFingerprint,
            snapshot.BaseInstructionsTokenEstimate,
            boundary);
        if (runtime != null)
            runtime.ContextUsageAnchor = anchor;
        return anchor;
    }

    private ContextTokenUsageEstimate EstimateContextTokens(
        string threadId,
        IReadOnlyList<ChatMessage> modelVisibleHistory,
        long latestContextTokens,
        PromptRequestSnapshot? requestSnapshot = null)
    {
        var persistedSnapshot = persistence.LoadContextUsageSnapshot(threadId);
        var displayOnly = persistedSnapshot?.Source is ProviderOccupancySource;
        var persistedEstimate = displayOnly ? null : persistedSnapshot;
        var estimate = ContextTokenUsageEstimator.Estimate(
            modelVisibleHistory,
            TryGetInMemoryContextUsageAnchor(threadId),
            persistence.LoadContextUsageAnchor(threadId),
            latestContextTokens,
            persistedEstimate?.Tokens,
            requestSnapshot?.RequestFingerprint,
            requestSnapshot?.ContextUsageFingerprint,
            requestSnapshot?.BaseInstructionsTokenEstimate,
            persistedEstimate?.Source,
            persistedEstimate?.IsEstimate ?? false,
            requestSnapshot?.EstimateRequestOverheadTokens() ?? 0);
        return displayOnly ? estimate with { PreserveMeasuredDisplay = true } : estimate;
    }

    private static IReadOnlyList<ChatMessage> PrepareProviderVisibleHistory(IReadOnlyList<ChatMessage> history) =>
        ModelRequestHistorySanitizer.Sanitize(history);

    private PreparedContextTokenEstimate PrepareContextTokenEstimate(
        string threadId,
        IReadOnlyList<ChatMessage> modelVisibleHistory,
        long latestContextTokens,
        PromptRequestSnapshot? requestSnapshot = null)
    {
        var preparedHistory = PrepareProviderVisibleHistory(modelVisibleHistory);
        var preparedSnapshot = ReferenceEquals(preparedHistory, modelVisibleHistory) || requestSnapshot is null
            ? requestSnapshot
            : RebasePromptRequestSnapshotMessages(requestSnapshot, preparedHistory);
        var estimate = EstimateContextTokens(
            threadId,
            preparedHistory,
            latestContextTokens,
            preparedSnapshot);
        var providerOptions = preparedSnapshot is null
            ? null
            : MaintenanceForkRunner.BuildOptions(preparedSnapshot);
        if (ProviderRequestContextScope.Current?.History is { } providerHistory
            && providerHistory.TryEstimateActiveContextTokens(
                preparedHistory,
                providerOptions,
                out var providerNativeTokens)
            && estimate.Source is not (
                "memory_anchor" or
                "persisted_anchor" or
                "prefix_adjusted_anchor"))
        {
            return new PreparedContextTokenEstimate(
                preparedHistory,
                preparedSnapshot,
                new ContextTokenUsageEstimate(
                    providerNativeTokens,
                    "provider_compacted_estimate",
                    EligibleForAutoCompact: true,
                    IsEstimate: true));
        }
        return new PreparedContextTokenEstimate(preparedHistory, preparedSnapshot, estimate);
    }

    private bool GoalsEnabled => Goals.Enabled;
}
