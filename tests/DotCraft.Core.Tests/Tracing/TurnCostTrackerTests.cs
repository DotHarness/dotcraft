using DotCraft.Configuration;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class TurnCostTrackerTests
{
    [Fact]
    public void Resolve_EstimatesEachTokenKindAtItsRate()
    {
        var tracker = new TurnCostTracker();
        tracker.AddUsage(new TokenUsageSnapshot(InputTokens: 1_000_000, OutputTokens: 100_000, CachedInputTokens: 600_000,
            ReasoningOutputTokens: 0, CacheWriteInputTokens: 100_000), isNewRequest: true);

        var cost = tracker.Resolve(new ModelPricing(InputPerMillion: 2m, OutputPerMillion: 10m, CacheReadPerMillion: 0.2m, CacheWritePerMillion: 2.5m));

        Assert.Equal(0.6m + 0.12m + 0.25m + 1m, cost);
    }

    [Fact]
    public void Resolve_FallsBackToInputRateForCacheTokens()
    {
        var tracker = new TurnCostTracker();
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 500_000, 0), isNewRequest: true);

        Assert.Equal(1m, tracker.Resolve(new ModelPricing(InputPerMillion: 1m, OutputPerMillion: 4m)));
    }

    [Fact]
    public void Resolve_PrefersReportedAmountPerRequest()
    {
        var tracker = new TurnCostTracker();
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), isNewRequest: true);
        tracker.AddReportedCost(0.03m);
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), isNewRequest: true);

        Assert.Equal(0.03m + 1m, tracker.Resolve(new ModelPricing(InputPerMillion: 1m, OutputPerMillion: 4m)));
        Assert.Null(tracker.Resolve(pricing: null));
    }

    [Fact]
    public void Resolve_IsNullWithoutRequestsOrWhenARequiredRateIsMissing()
    {
        Assert.Null(new TurnCostTracker().Resolve(new ModelPricing(1m, 1m)));

        var tracker = new TurnCostTracker();
        tracker.AddUsage(new TokenUsageSnapshot(10, 5, 0, 0), isNewRequest: true);
        Assert.Null(tracker.Resolve(new ModelPricing(InputPerMillion: 1m, OutputPerMillion: null)));
    }
}
