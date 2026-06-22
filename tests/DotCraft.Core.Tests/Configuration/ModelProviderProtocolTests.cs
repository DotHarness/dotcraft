using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Tools;

namespace DotCraft.Tests.Configuration;

public sealed class ModelProviderProtocolTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openai")]
    [InlineData(" OPENAI ")]
    [InlineData("openai-chat-completions")]
    public void Normalize_OpenAILegacyAliasReturnsChatCompletions(string? protocol)
    {
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, ModelProviderProtocols.Normalize(protocol));
    }

    [Fact]
    public void Normalize_OpenAIResponsesReturnsCanonicalProtocol()
    {
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, ModelProviderProtocols.Normalize("openai-responses"));
    }

    [Fact]
    public void AppConfig_DeferredLoadingDefaultsToAutoAndIgnoresLegacyEnabled()
    {
        var config = JsonSerializer.Deserialize<AppConfig>(
            """
            {
              "Tools": {
                "DeferredLoading": {
                  "Enabled": true
                }
              }
            }
            """,
            AppConfig.SerializerOptions)!;

        Assert.Equal(AppConfig.DeferredLoadingStrategy.Auto, config.Tools.DeferredLoading.Strategy);
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
