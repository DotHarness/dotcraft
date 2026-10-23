using DotCraft.Configuration;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelProviderProtocolTests
{
    [Theory]
    [InlineData(null, ModelProviderProtocols.OpenAIChatCompletions)]
    [InlineData("", ModelProviderProtocols.OpenAIChatCompletions)]
    [InlineData("openai-responses", ModelProviderProtocols.OpenAIResponses)]
    public void Normalize_ReturnsCanonicalProtocol(string? protocol, string expected)
    {
        Assert.Equal(expected, ModelProviderProtocols.Normalize(protocol));
    }

    [Theory]
    [InlineData(AppConfig.DeferredLoadingStrategy.Auto, ModelProviderProtocols.OpenAIChatCompletions, "Simulated")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Auto, ModelProviderProtocols.Anthropic, "Native")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Simulated, ModelProviderProtocols.OpenAIResponses, "Simulated")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Native, ModelProviderProtocols.Anthropic, "Native")]
    public void DeferredLoadingStrategy_ResolvesAtProtocolLevel(
        AppConfig.DeferredLoadingStrategy strategy,
        string protocol,
        string expected)
    {
        var mode = DeferredToolLoadingPlanner.ResolveMode(new AppConfig.DeferredLoadingConfig
        {
            Strategy = strategy
        }, protocol);

        Assert.Equal(expected, mode.ToString());
    }

    [Fact]
    public void DeferredLoadingStrategy_NativeRejectsChatCompletionsProtocol()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DeferredToolLoadingPlanner.ResolveMode(new AppConfig.DeferredLoadingConfig
            {
                Strategy = AppConfig.DeferredLoadingStrategy.Native
            }, ModelProviderProtocols.OpenAIChatCompletions));
    }
}
