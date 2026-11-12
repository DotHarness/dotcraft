using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.OpenRouter;

public sealed class RoutingTests
{
    [Theory]
    [InlineData("anthropic/claude-opus-5.5", "anthropic")]
    [InlineData("openai/gpt-6-astra", "openai-responses")]
    [InlineData("deepseek/deepseek-chat", "openai-chat-completions")]
    public void AutoUsesAdvertisedFamilyPreference(string id, string expected)
    {
        var model = new ModelCatalogEntry { Id = id, SupportedProtocols = ["anthropic", "openai-responses", "openai-chat-completions"] };
        Assert.Equal(expected, OpenRouterModelCatalog.SelectProtocol(model, "auto"));
        Assert.Equal("openai-chat-completions", OpenRouterModelCatalog.SelectProtocol(model, "openai-chat-completions"));
        model.SupportedProtocols = ["openai-chat-completions"];
        Assert.Equal("openai-chat-completions", OpenRouterModelCatalog.SelectProtocol(model, "auto"));
        Assert.Throws<ArgumentException>(() => OpenRouterModelCatalog.SelectProtocol(model, "anthropic"));
    }

    internal static EffectiveModelRuntime Runtime() => new("test", "", "auto", "OpenRouter", "test-key",
        OpenRouterModelCatalog.DefaultEndpoint, 20, null, new())
        { BackendId = "openrouter", ConfiguredProtocol = "auto", ConfiguredEndPoint = OpenRouterModelCatalog.DefaultEndpoint };
    internal static OpenRouterProvider Create(EffectiveModelRuntime? runtime = null, HttpClient? http = null, IProviderCredentialStore? credentials = null) =>
        new(new ModelBackendContext(runtime ?? Runtime(), credentials ?? new Credentials(), CancellationToken.None), catalogClient: http);

    internal sealed class Credentials : IProviderCredentialStore
    {
        internal JsonElement? Value;
        public Task<JsonElement?> ReadAsync(CancellationToken ct) => Task.FromResult(Value);
        public async Task<JsonElement?> ModifyAsync(Func<JsonElement?, CancellationToken, Task<JsonElement?>> update, CancellationToken ct)
        { var value = await update(Value, ct); ct.ThrowIfCancellationRequested(); return Value = value; }
        public Task DeleteAsync(CancellationToken ct) { Value = null; return Task.CompletedTask; }
    }
}
