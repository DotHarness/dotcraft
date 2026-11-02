using DotCraft.Agents;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests;

public sealed class ModelCostCalculatorTests
{
    [Fact]
    public void Resolve_PricesAllTokenKindsWithoutChargingReasoningTwice()
    {
        var usage = new ProviderHttpUsage(1_000_000, 100_000, 600_000, 100_000, 50_000);
        var pricing = new ModelPricing(2m, 10m, 0.2m, 2.5m);

        Assert.Equal((0.6m + 1m + 0.12m + 0.25m) * 2.5m,
            ModelCostCalculator.Resolve(pricing, usage, 2.5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.125)]
    public void Resolve_ReportedAmountOverridesMissingUsageAndPrices(double amount)
    {
        Assert.Equal((decimal)amount, ModelCostCalculator.Resolve(null, null, 2.5m, (decimal)amount));
    }

    [Fact]
    public void Resolve_UsesInputPriceForUndeclaredCachePricesButPreservesExplicitZero()
    {
        var usage = new ProviderHttpUsage(1_000_000, 0, 500_000, 100_000);

        Assert.Equal(2m, ModelCostCalculator.Resolve(new ModelPricing(2m, 10m), usage));
        Assert.Equal(0.8m, ModelCostCalculator.Resolve(new ModelPricing(2m, 10m, 0m, 0m), usage));
    }

    [Fact]
    public void Resolve_UnknownRequiredPricesOrUsageRemainUnknown()
    {
        Assert.Null(ModelCostCalculator.Resolve(null, new ProviderHttpUsage(1, 0)));
        Assert.Null(ModelCostCalculator.Resolve(new ModelPricing(1m, 2m), null));
        Assert.Null(ModelCostCalculator.Resolve(new ModelPricing(1m, null), new ProviderHttpUsage(10, 1)));
        Assert.Equal(0.00001m, ModelCostCalculator.Resolve(new ModelPricing(1m, null), new ProviderHttpUsage(10, 0)));
    }
}
