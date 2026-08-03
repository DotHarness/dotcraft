using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;
using Xunit;

namespace DotCraft.Tests.Configuration;

public sealed class ModelProviderProtocolTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openai-chat-completions")]
    public void Normalize_DefaultOrCanonicalChatCompletionsReturnsCanonicalProtocol(string? protocol)
    {
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.Normalize(protocol));
    }

    [Fact]
    public void Normalize_OpenAIResponsesReturnsCanonicalProtocol()
    {
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, ModelProviderProtocols.Normalize("openai-responses"));
    }


    [Theory]
    [InlineData(AppConfig.DeferredLoadingStrategy.Auto, ModelProviderProtocols.OpenAIChatCompletions, "Simulated")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Auto, ModelProviderProtocols.OpenAIResponses, "Native")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Auto, ModelProviderProtocols.Anthropic, "Native")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Simulated, ModelProviderProtocols.OpenAIResponses, "Simulated")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Simulated, ModelProviderProtocols.Anthropic, "Simulated")]
    [InlineData(AppConfig.DeferredLoadingStrategy.Native, ModelProviderProtocols.OpenAIResponses, "Native")]
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

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAIChatCompletions)]
    public void DeferredLoadingStrategy_NativeRejectsNonResponsesProtocol(string protocol)
    {
        Assert.Throws<InvalidOperationException>(() =>
            DeferredToolLoadingPlanner.ResolveMode(new AppConfig.DeferredLoadingConfig
            {
                Strategy = AppConfig.DeferredLoadingStrategy.Native
            }, protocol));
    }
}
