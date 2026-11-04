using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicThinkingBindingSupportTests
{
    [Theory]
    [InlineData("https://api.anthropic.com", "claude-opus-5-5-20261001", true)]
    [InlineData("https://api.anthropic.com", "claude-opus-5", true)]
    [InlineData("https://openrouter.ai/api", "anthropic/claude-opus-5.5", true)]
    [InlineData("https://openrouter.ai/api", "~anthropic/claude-sonnet-5.5", true)]
    [InlineData("https://openrouter.ai/api", "anthropic/claude-opus-5", false)]
    [InlineData("https://api.anthropic.com", "claude-opus-4-8", false)]
    public void Catalog_EnablesBindingOnlyForVerifiedModelTransport(string endpoint, string model, bool expected)
    {
        var adapter = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(endpoint, model);

        Assert.NotNull(adapter);
        Assert.Equal(expected, adapter.SupportsThinkingBlockBinding);
    }

    [Theory]
    [InlineData("https://proxy.example", true)]
    [InlineData("https://api.anthropic.com", false)]
    public void Catalog_ExplicitTransportSupportOverridesDefaults(string endpoint, bool supported)
    {
        var path = Path.Combine(Path.GetTempPath(), $"thinking-binding-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, $$$"""
                {"anthropicThinking":{"adapters":[{
                  "models":["claude-opus-5-5"],
                  "thinking":{"type":"adaptive","supportsBlockBinding":{{{(supported ? "true" : "false")}}}}
                }]}}
                """);

            var adapter = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(endpoint, "claude-opus-5-5", path);

            Assert.NotNull(adapter);
            Assert.Equal(supported, adapter.SupportsThinkingBlockBinding);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
