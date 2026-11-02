using DotCraft.Configuration;

namespace DotCraft.Agents;

public static class ModelCostCalculator
{
    /// <summary>Resolves one request's USD cost; a reported amount, including zero, takes precedence over estimation.</summary>
    public static decimal? Resolve(ModelPricing? pricing, ProviderHttpUsage? usage,
        decimal multiplier = 1m, decimal? reportedCostUsd = null)
    {
        if (reportedCostUsd is { } reported)
            return reported;
        if (pricing is null || usage is null)
            return null;

        var fresh = Math.Max(0, usage.InputTokens - usage.CachedInputTokens - usage.CacheWriteTokens);
        decimal total = 0;
        foreach (var (tokens, rate) in new[]
                 {
                     (fresh, pricing.InputPerMillion),
                     (usage.CachedInputTokens, pricing.CacheReadPerMillion ?? pricing.InputPerMillion),
                     (usage.CacheWriteTokens, pricing.CacheWritePerMillion ?? pricing.InputPerMillion),
                     (usage.OutputTokens, pricing.OutputPerMillion)
                 })
        {
            if (tokens <= 0)
                continue;
            if (rate is not { } perMillion)
                return null;
            total += tokens * perMillion;
        }
        return total * multiplier / 1_000_000m;
    }
}
