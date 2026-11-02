using DotCraft.Configuration;

namespace DotCraft.Tracing;

/// <summary>Collects a Turn's model requests so their cost can be resolved once its model is known.</summary>
internal sealed class TurnCostTracker
{
    private readonly List<Request> _requests = [];

    public void AddUsage(TokenUsageSnapshot delta, bool isNewRequest)
    {
        if (isNewRequest || _requests.Count == 0)
            _requests.Add(new Request());
        _requests[^1].Add(delta);
    }

    /// <summary>Attributes a provider-billed amount to the latest request, which reports it after its usage.</summary>
    public void AddReportedCost(decimal amount)
    {
        if (_requests.Count == 0)
            _requests.Add(new Request());
        _requests[^1].Reported = (_requests[^1].Reported ?? 0) + amount;
    }

    /// <summary>Reported amounts where present, otherwise catalog estimates; null when any request cannot be priced.</summary>
    public decimal? Resolve(ModelPricing? pricing)
    {
        if (_requests.Count == 0)
            return null;

        decimal total = 0;
        foreach (var request in _requests)
        {
            if (request.Reported is { } reported)
            {
                total += reported;
                continue;
            }

            if (Estimate(pricing, request) is not { } estimate)
                return null;
            total += estimate;
        }

        return total;
    }

    private static decimal? Estimate(ModelPricing? pricing, Request request)
    {
        if (pricing is null)
            return null;

        var cacheRead = pricing.CacheReadPerMillion ?? pricing.InputPerMillion;
        var cacheWrite = pricing.CacheWritePerMillion ?? pricing.InputPerMillion;
        var fresh = Math.Max(0, request.Input - request.Cached - request.CacheWrite);
        decimal total = 0;
        foreach (var (tokens, rate) in new[]
                 {
                     (fresh, pricing.InputPerMillion),
                     (request.Cached, cacheRead),
                     (request.CacheWrite, cacheWrite),
                     (request.Output, pricing.OutputPerMillion)
                 })
        {
            if (tokens <= 0)
                continue;
            if (rate is not { } perMillion)
                return null;
            total += tokens * perMillion;
        }

        return total / 1_000_000m;
    }

    private sealed class Request
    {
        public long Input { get; private set; }
        public long Output { get; private set; }
        public long Cached { get; private set; }
        public long CacheWrite { get; private set; }
        public decimal? Reported { get; set; }

        public void Add(TokenUsageSnapshot delta)
        {
            Input += delta.InputTokens;
            Output += delta.OutputTokens;
            Cached += delta.CachedInputTokens;
            CacheWrite += delta.CacheWriteInputTokens;
        }
    }
}
