using DotCraft.Agents;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIServiceTierPricingTests
{
    [Fact]
    public void Codex_keeps_the_requested_ultrafast_tier_when_the_response_echoes_default()
    {
        var requested = OpenAIServiceTierPricing.GetMultiplier(
            "gpt-6.2-astra", "ultrafast", "ultrafast", isCodex: true);
        var echoed = OpenAIServiceTierPricing.GetMultiplier(
            "gpt-6.2-astra", "ultrafast", "default", isCodex: true);

        Assert.Equal(requested, echoed);
    }

    [Fact]
    public void Response_tier_wins_over_the_requested_tier()
    {
        var standard = OpenAIServiceTierPricing.GetMultiplier("gpt-6.2-astra", null, null, isCodex: false);
        var echoed = OpenAIServiceTierPricing.GetMultiplier(
            "gpt-6.2-astra", "ultrafast", "default", isCodex: false);

        Assert.Equal(standard, echoed);
    }
}
