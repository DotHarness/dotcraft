using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
        => ThreadAccess.TryGetContextUsageSnapshot(threadId);

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

        return RebasePromptRequestSnapshotForManualCompaction(
            template,
            threadId,
            currentHistory,
            estimatedInputTokens,
            invalidReason);
    }

    private static PromptRequestSnapshot RebasePromptRequestSnapshotForManualCompaction(
        PromptRequestSnapshot template,
        string threadId,
        IReadOnlyList<ChatMessage> currentHistory,
        int? estimatedInputTokens,
        string? invalidReason)
    {
        var capturedMessages = MessageGrouper
            .NormalizeFunctionCallArguments(currentHistory)
            .Select(message => message.Clone())
            .ToArray();

        return template with
        {
            Messages = capturedMessages,
            MessageFingerprint = MessageTokenEstimator.ComputePrefixFingerprint(capturedMessages, capturedMessages.Length),
            ThreadId = threadId,
            TurnId = null,
            EstimatedInputTokens = estimatedInputTokens ?? template.EstimatedInputTokens,
            SnapshotSource = PromptRequestSnapshotSources.ManualRebased,
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
        bool isEstimate = false)
    {
        var pipeline = GetCompactionPipelineForThread(threadId);
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
        CancellationToken ct = default)
    {
        var normalizedTokens = Math.Max(0, tokens);
        if (_runtimeRegistry.TryGetRuntime(threadId, out var ephemeralRuntime) && ephemeralRuntime.Thread.Ephemeral)
        {
            ephemeralRuntime.ContextUsageAnchor = anchor;
            return CreateContextUsageSnapshot(threadId, normalizedTokens, source, isEstimate);
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

        return CreateContextUsageSnapshot(threadId, normalizedTokens, source, isEstimate);
    }

    private async Task<ContextUsageSnapshot> SaveReplacementContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        string source,
        CancellationToken ct = default)
    {
        var snapshot = await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            source,
            isEstimate: true,
            ct: ct);
        ClearContextUsageAnchor(threadId);
        return snapshot;
    }

    private async Task<ContextUsageSnapshot> SaveCurrentRequestEstimateAsync(
        string threadId,
        long tokens,
        CancellationToken ct = default)
    {
        var anchor = TryGetInMemoryContextUsageAnchor(threadId)
            ?? persistence.LoadContextUsageAnchor(threadId);
        return await SaveContextUsageSnapshotAsync(
            threadId,
            tokens,
            anchor,
            source: "estimate",
            isEstimate: true,
            ct: ct);
    }

    private Task<ContextUsageSnapshot> SavePreparedContextEstimateAsync(
        string threadId,
        ContextTokenUsageEstimate estimate,
        CancellationToken ct = default) =>
        string.Equals(
            estimate.Source,
            "provider_compacted_estimate",
            StringComparison.Ordinal)
            ? SaveContextUsageSnapshotAsync(
                threadId,
                estimate.Tokens,
                estimate.Source,
                isEstimate: true,
                ct)
            : SaveCurrentRequestEstimateAsync(threadId, estimate.Tokens, ct);

    private ContextUsageAnchor? UpdateContextUsageAnchor(string threadId, long anchorTokens)
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
            ContextUsageAnchorBoundary.Request);
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
        var persistedAnchor = persistence.LoadContextUsageAnchor(threadId);
        return ContextTokenUsageEstimator.Estimate(
            modelVisibleHistory,
            TryGetInMemoryContextUsageAnchor(threadId),
            persistedAnchor,
            latestContextTokens,
            persistedSnapshot?.Tokens,
            requestSnapshot?.RequestFingerprint,
            requestSnapshot?.ContextUsageFingerprint,
            requestSnapshot?.BaseInstructionsTokenEstimate,
            persistedSnapshot?.Source,
            persistedSnapshot?.IsEstimate ?? false);
    }

    private static IReadOnlyList<ChatMessage> PrepareProviderVisibleHistory(IReadOnlyList<ChatMessage> history)
    {
        var repairedHistory = ModelRequestHistorySanitizer.Sanitize(history);
        return ImageContentSanitizingChatClient.ReplaceHistoricalToolImagesWithDescriptions(repairedHistory);
    }

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
