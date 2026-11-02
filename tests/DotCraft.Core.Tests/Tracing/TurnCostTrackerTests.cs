using DotCraft.Configuration;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class TurnCostTrackerTests
{
    [Fact]
    public void Resolve_PrefersReportedAmountPerRequest()
    {
        var tracker = new TurnCostTracker();
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), isNewRequest: true);
        var reported = new ChatResponseUpdate();
        ProviderReportedCost.Attach(reported, 0.03m);
        tracker.ObserveUpdate(reported, requestIndex: null);
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), isNewRequest: true);

        Assert.Equal(0.03m + 1m, tracker.Resolve(new ModelPricing(InputPerMillion: 1m, OutputPerMillion: 4m)));
        Assert.Null(tracker.Resolve(pricing: null));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Resolve_AppliesFinalMultiplierOnceToItsRequest(bool metadataFirst)
    {
        var tracker = new TurnCostTracker();
        var adjustment = new ChatResponseUpdate();
        ProviderCostMultiplier.Attach(adjustment, 2.5m);
        if (metadataFirst)
            tracker.ObserveUpdate(adjustment, 1);
        tracker.AddUsage(new TokenUsageSnapshot(500_000, 0, 0, 0), true, 1);
        tracker.AddUsage(new TokenUsageSnapshot(500_000, 0, 0, 0), false, 1);
        tracker.ObserveUpdate(adjustment, 1);
        tracker.ObserveUpdate(adjustment, 1);
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), true, 2);

        Assert.Equal(3.5m, tracker.Resolve(new ModelPricing(1m, 4m)));
    }

    [Fact]
    public void Resolve_FinalStandardTierReplacesRequestedPremiumAndZeroReportedCostWins()
    {
        var tracker = new TurnCostTracker();
        var adjustment = new ChatResponseUpdate();
        ProviderCostMultiplier.Attach(adjustment, 2m);
        tracker.ObserveUpdate(adjustment, 1);
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), true, 1);
        ProviderCostMultiplier.Attach(adjustment, 1m);
        tracker.ObserveUpdate(adjustment, 1);
        tracker.AddUsage(new TokenUsageSnapshot(1_000_000, 0, 0, 0), true, 2);
        var reported = new ChatResponseUpdate();
        ProviderCostMultiplier.Attach(reported, 2.5m);
        ProviderReportedCost.Attach(reported, 0m);
        tracker.ObserveUpdate(reported, 2);

        Assert.Equal(1m, tracker.Resolve(new ModelPricing(1m, 4m)));
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
