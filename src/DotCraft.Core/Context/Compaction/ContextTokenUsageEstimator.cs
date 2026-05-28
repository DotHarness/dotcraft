using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal sealed record ContextTokenUsageEstimate(long Tokens, string Source);

internal static class ContextTokenUsageEstimator
{
    public static ContextTokenUsageEstimate Estimate(
        IReadOnlyList<ChatMessage> history,
        ContextUsageAnchor? memoryAnchor,
        ContextUsageAnchor? persistedAnchor,
        long latestProviderTokens,
        long? persistedTokens)
    {
        if (ContextUsageTokenCounter.EstimateFromAnchor(memoryAnchor, history) is { } memoryAnchored)
        {
            return new ContextTokenUsageEstimate(
                Math.Max(memoryAnchored, Math.Max(0, latestProviderTokens)),
                "memory_anchor");
        }

        if (ContextUsageTokenCounter.EstimateFromAnchor(persistedAnchor, history) is { } persistedAnchored)
        {
            return new ContextTokenUsageEstimate(
                Math.Max(persistedAnchored, Math.Max(0, latestProviderTokens)),
                "persisted_anchor");
        }

        var estimatedTokens = MessageTokenEstimator.Estimate(history);
        var bestTokens = (long)estimatedTokens;
        var source = "estimate";

        if (latestProviderTokens > bestTokens)
        {
            bestTokens = latestProviderTokens;
            source = "provider";
        }

        if (persistedTokens is > 0 && persistedTokens.Value > bestTokens)
        {
            bestTokens = persistedTokens.Value;
            source = "persisted";
        }

        return new ContextTokenUsageEstimate(bestTokens, source);
    }
}
