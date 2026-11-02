using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

internal sealed class TurnCostTracker
{
    private readonly List<Request> _requests = [];

    public void AddUsage(TokenUsageSnapshot delta, bool isNewRequest, int? requestIndex = null)
    {
        var request = GetRequest(requestIndex, isNewRequest);
        request.Usage = new ProviderHttpUsage(
            (request.Usage?.InputTokens ?? 0) + delta.InputTokens,
            (request.Usage?.OutputTokens ?? 0) + delta.OutputTokens,
            (request.Usage?.CachedInputTokens ?? 0) + delta.CachedInputTokens,
            (request.Usage?.CacheWriteTokens ?? 0) + delta.CacheWriteInputTokens);
    }

    public void ObserveUpdate(ChatResponseUpdate update, int? requestIndex)
    {
        var reported = ProviderReportedCost.TryGet(update.AdditionalProperties);
        var multiplier = ProviderCostMultiplier.TryGet(update.AdditionalProperties);
        if (reported is null && multiplier is null)
            return;
        var request = GetRequest(requestIndex);
        if (reported is { } amount)
            request.Reported = (request.Reported ?? 0) + amount;
        if (multiplier is { } factor)
            request.Multiplier = factor;
    }

    public decimal? Resolve(ModelPricing? pricing)
    {
        if (_requests.Count == 0)
            return null;
        decimal total = 0;
        foreach (var request in _requests)
        {
            if (ModelCostCalculator.Resolve(pricing, request.Usage, request.Multiplier, request.Reported) is not { } cost)
                return null;
            total += cost;
        }
        return total;
    }

    private Request GetRequest(int? index, bool newRequest = false)
    {
        if (_requests.Count == 0 || (index.HasValue
                ? _requests[^1].Index != index
                : newRequest && _requests[^1].Usage is not null))
            _requests.Add(new Request(index));
        return _requests[^1];
    }

    private sealed class Request(int? index)
    {
        public int? Index { get; } = index;
        public ProviderHttpUsage? Usage { get; set; }
        public decimal? Reported { get; set; }
        public decimal Multiplier { get; set; } = 1m;
    }
}
