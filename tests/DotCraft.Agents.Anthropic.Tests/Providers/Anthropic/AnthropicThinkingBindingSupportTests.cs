using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicThinkingBindingSupportTests
{
    [Theory]
    [InlineData("~anthropic/claude-fable-5.1@default", true)]
    [InlineData("anthropic/claude-mythos-5-1-20260901", true)]
    [InlineData("claude-fable-5-20260901", false)]
    [InlineData("claude-sonnet-6", true)]
    public void Catalog_DistinguishesClaudeVersionsFromSnapshotDates(string model, bool supported)
    {
        var adapter = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
            "https://proxy.example", model, useAdaptiveFallback: true);

        Assert.Equal(supported, adapter!.SupportsThinkingBlockBinding);
    }

    [Theory]
    [InlineData("proxy.example", "claude-opus-5-5", false)]
    [InlineData("other.example", "claude-opus-5-5", true)]
    [InlineData("proxy.example", "claude-fable-5-1", true)]
    [InlineData("proxy.example", "custom-reasoner", true)]
    [InlineData("other.example", "custom-reasoner", false)]
    public void Catalog_LastMatchingBindingOverrideRequiresBothSelectorsWhenSpecified(
        string host, string model, bool supported)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {"anthropicThinking":{"adapters":[
                  {"endpoints":["proxy.example"],"thinking":{"supportsBlockBinding":true}},
                  {"endpoints":["proxy.example"],"models":["claude-opus-5-5"],"thinking":{"supportsBlockBinding":false}}
                ]}}
                """);

            var adapter = ModelThinkingAdapterCatalog.ResolveAnthropicThinkingAdapter(
                $"https://{host}/v1", model, path, useAdaptiveFallback: true);

            Assert.Equal(supported, adapter!.SupportsThinkingBlockBinding);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
