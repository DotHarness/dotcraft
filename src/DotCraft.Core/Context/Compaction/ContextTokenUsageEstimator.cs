using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal sealed record ContextTokenUsageEstimate(long Tokens, string Source, bool EligibleForAutoCompact, bool IsEstimate);

internal static class ContextTokenUsageEstimator
{
    public static ContextTokenUsageEstimate Estimate(
        IReadOnlyList<ChatMessage> history,
        ContextUsageAnchor? memoryAnchor,
        ContextUsageAnchor? persistedAnchor,
        long latestContextTokens,
        long? persistedDisplayTokens,
        string? requestFingerprint = null,
        string? contextUsageFingerprint = null,
        int? baseInstructionsTokenEstimate = null,
        string? persistedDisplaySource = null,
        bool persistedDisplayIsEstimate = false)
    {
        const bool requireRequestFingerprint = true;
        var latestProviderTokens = Math.Max(0, latestContextTokens);
        if (ContextUsageTokenCounter.EstimateFromAnchorDetailed(
                memoryAnchor,
                history,
                requestFingerprint,
                requireRequestFingerprint,
                contextUsageFingerprint,
                baseInstructionsTokenEstimate) is { } memoryAnchored)
        {
            return new ContextTokenUsageEstimate(
                SelectAnchoredTokens(memoryAnchored, latestProviderTokens),
                memoryAnchored.UsedBaseInstructionsAdjustment ? "prefix_adjusted_anchor" : "memory_anchor",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        if (ContextUsageTokenCounter.EstimateFromAnchorDetailed(
                persistedAnchor,
                history,
                requestFingerprint,
                requireRequestFingerprint,
                contextUsageFingerprint,
                baseInstructionsTokenEstimate) is { } persistedAnchored)
        {
            return new ContextTokenUsageEstimate(
                SelectAnchoredTokens(persistedAnchored, latestProviderTokens),
                persistedAnchored.UsedBaseInstructionsAdjustment ? "prefix_adjusted_anchor" : "persisted_anchor",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        if (latestProviderTokens > 0)
        {
            return new ContextTokenUsageEstimate(
                latestProviderTokens,
                "unverified_provider_context",
                EligibleForAutoCompact: false,
                IsEstimate: false);
        }

        if (persistedDisplayTokens is > 0 && IsProviderContextSource(persistedDisplaySource, persistedDisplayIsEstimate))
        {
            return new ContextTokenUsageEstimate(
                persistedDisplayTokens.Value,
                "persisted_provider_context",
                EligibleForAutoCompact: false,
                IsEstimate: false);
        }

        var estimatedTokens = (long)MessageTokenEstimator.Estimate(history);
        if (persistedDisplayTokens is > 0 && IsReplacementHistoryEstimateSource(persistedDisplaySource, persistedDisplayIsEstimate))
        {
            return new ContextTokenUsageEstimate(
                Math.Max(estimatedTokens, persistedDisplayTokens.Value),
                persistedDisplaySource!,
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        var hasProviderLineage = memoryAnchor is not null
            || persistedAnchor is not null;
        if (hasProviderLineage)
        {
            return new ContextTokenUsageEstimate(
                estimatedTokens,
                "estimate_unverified_provider_lineage",
                EligibleForAutoCompact: false,
                IsEstimate: true);
        }

        if (estimatedTokens <= 0 && persistedDisplayTokens is > 0)
        {
            return new ContextTokenUsageEstimate(
                persistedDisplayTokens.Value,
                "persisted_display",
                EligibleForAutoCompact: false,
                IsEstimate: persistedDisplayIsEstimate);
        }

        return new ContextTokenUsageEstimate(
            estimatedTokens,
            "estimate",
            EligibleForAutoCompact: true,
            IsEstimate: true);
    }

    private static bool IsProviderContextSource(string? source, bool isEstimate) =>
        !isEstimate
        && string.Equals(source, "provider_context", StringComparison.OrdinalIgnoreCase);

    private static long SelectAnchoredTokens(ContextUsageAnchorEstimate anchored, long latestProviderTokens)
    {
        if (latestProviderTokens <= 0)
            return anchored.Tokens;

        if (!anchored.UsedBaseInstructionsAdjustment)
            return Math.Max(anchored.Tokens, latestProviderTokens);

        var adjustedProviderTokens = Math.Max(0, latestProviderTokens + anchored.BaseInstructionsTokenDelta);
        return Math.Max(anchored.Tokens, adjustedProviderTokens);
    }

    private static bool IsReplacementHistoryEstimateSource(string? source, bool isEstimate) =>
        isEstimate
        && (string.Equals(source, "history_estimate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, "compacted_estimate", StringComparison.OrdinalIgnoreCase));
}
