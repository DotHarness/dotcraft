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
        string? requestFingerprint = null)
    {
        const bool requireRequestFingerprint = true;
        if (ContextUsageTokenCounter.EstimateFromAnchor(
                memoryAnchor,
                history,
                requestFingerprint,
                requireRequestFingerprint) is { } memoryAnchored)
        {
            return new ContextTokenUsageEstimate(
                Math.Max(memoryAnchored, Math.Max(0, latestContextTokens)),
                "memory_anchor",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        if (ContextUsageTokenCounter.EstimateFromAnchor(
                persistedAnchor,
                history,
                requestFingerprint,
                requireRequestFingerprint) is { } persistedAnchored)
        {
            return new ContextTokenUsageEstimate(
                Math.Max(persistedAnchored, Math.Max(0, latestContextTokens)),
                "persisted_anchor",
                EligibleForAutoCompact: true,
                IsEstimate: true);
        }

        var estimatedTokens = MessageTokenEstimator.Estimate(history);
        var bestTokens = (long)estimatedTokens;
        var source = "estimate";
        var isEstimate = true;

        if (latestContextTokens > bestTokens)
        {
            bestTokens = latestContextTokens;
            source = "provider_context";
            isEstimate = false;
        }

        if (bestTokens <= 0 && persistedDisplayTokens is > 0)
        {
            return new ContextTokenUsageEstimate(
                persistedDisplayTokens.Value,
                "persisted_display",
                EligibleForAutoCompact: false,
                IsEstimate: false);
        }

        return new ContextTokenUsageEstimate(
            bestTokens,
            source,
            EligibleForAutoCompact: true,
            IsEstimate: isEstimate);
    }
}
