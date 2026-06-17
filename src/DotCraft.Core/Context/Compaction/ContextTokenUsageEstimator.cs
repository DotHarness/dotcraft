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
                Math.Max(memoryAnchored.Tokens, latestProviderTokens),
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
                Math.Max(persistedAnchored.Tokens, latestProviderTokens),
                persistedAnchored.UsedBaseInstructionsAdjustment ? "prefix_adjusted_anchor" : "persisted_anchor",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        var estimatedTokens = (long)MessageTokenEstimator.Estimate(history);
        var hasProviderLineage = memoryAnchor is not null
            || persistedAnchor is not null
            || latestProviderTokens > 0
            || IsProviderContextSource(persistedDisplaySource, persistedDisplayIsEstimate);

        if (latestProviderTokens > 0)
        {
            return new ContextTokenUsageEstimate(
                Math.Max(estimatedTokens, latestProviderTokens),
                "estimate_unverified_provider_context",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        if (persistedDisplayTokens is > 0 && IsProviderContextSource(persistedDisplaySource, persistedDisplayIsEstimate))
        {
            return new ContextTokenUsageEstimate(
                persistedDisplayTokens.Value,
                "persisted_provider_context",
                EligibleForAutoCompact: false,
                IsEstimate: false);
        }

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
}
