using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Xunit;

namespace DotCraft.Tests.OpenRouter;

public sealed class RuntimeTests
{
    [Fact]
    public void OneInstanceResolvesModelProtocolsBeforeCoreDefaultsAndCompaction()
    {
        var registry = new ContributionRegistry();
        using var resolver = new ModelBackendResolver(new ModelProviderRegistry([]), registry, Path.GetTempPath(), [new OpenRouterBackend()]);
        var config = new AppConfig { RuntimeResolver = resolver, ProviderId = "router", Providers = new()
        { ["router"] = new() { BackendId = "openrouter", ApiKey = "test", Protocol = "auto" } } };
        var claude = ModelProviderResolver.ResolveMain(config, "router", "anthropic/claude-opus-5.5");
        var gpt = ModelProviderResolver.ResolveMain(config, "router", "openai/gpt-6-astra");
        Assert.Equal("anthropic", claude.Protocol);
        Assert.Equal("openai-responses", gpt.Protocol);
        Assert.Equal(claude, resolver.Normalize(claude));
        Assert.Same(resolver.Resolve(claude), resolver.Resolve(gpt));
        var preference = ModelPreferenceRules.CreateDefault(config, "router", claude.Model);
        Assert.True(preference.Reasoning.Enabled);
        Assert.Equal(ModelReasoningEffort.Medium, preference.Reasoning.Effort);
        Assert.NotNull(ModelPreferenceRules.ValidateReasoning(config, "router", claude.Model, new() { Enabled = false }));
        Assert.Equal(1_000_000, ModelCatalog.ResolveCompactionConfig(config, claude.Model, claude.ModelMetadata!.ContextWindow).ContextWindow);
        Assert.Throws<InvalidOperationException>(() => registry.Add<IModelBackendSource>(new Source()));
        Assert.Single(resolver.GetBackends());
    }

    private sealed class Source : IModelBackendSource
    {
        public IModelBackend Backend { get; } = new OpenRouterBackend();
    }
}
