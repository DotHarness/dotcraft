using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicThinkingBindingSupportTests
{
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
