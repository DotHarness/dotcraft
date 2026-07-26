using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

public sealed class AnthropicClientProviderTests
{
    [Fact]
    public void GetAnthropicClient_CacheKeyIncludesNetworkTimeout()
    {
        var provider = new AnthropicClientProvider();
        var baseRuntime = Runtime(ModelProviderProtocols.Anthropic, networkTimeoutSeconds: 600);
        var sameRuntime = Runtime(ModelProviderProtocols.Anthropic, networkTimeoutSeconds: 600);
        var differentTimeoutRuntime = Runtime(ModelProviderProtocols.Anthropic, networkTimeoutSeconds: 900);

        var first = provider.GetAnthropicClient(baseRuntime);
        var same = provider.GetAnthropicClient(sameRuntime);
        var differentTimeout = provider.GetAnthropicClient(differentTimeoutRuntime);

        Assert.Same(first, same);
        Assert.NotSame(first, differentTimeout);
    }

    [Fact]
    public void GetAnthropicClient_DisablesSdkRequestRetries()
    {
        var provider = new AnthropicClientProvider();

        var client = provider.GetAnthropicClient(Runtime(ModelProviderProtocols.Anthropic));

        Assert.Equal(0, client.MaxRetries.GetValueOrDefault());
        Assert.True(client.MaxRetries.HasValue);
    }

    [Fact]
    public void GetAnthropicClient_RejectsNonAnthropicProtocol()
    {
        var provider = new AnthropicClientProvider();
        var runtime = Runtime(ModelProviderProtocols.OpenAI);

        Assert.Throws<ArgumentException>(() => provider.GetAnthropicClient(runtime));
    }

    [Fact]
    public void GetAnthropicClient_RejectsMissingApiKey()
    {
        var provider = new AnthropicClientProvider();
        var runtime = Runtime(ModelProviderProtocols.Anthropic) with { ApiKey = " " };

        Assert.Throws<ArgumentException>(() => provider.GetAnthropicClient(runtime));
    }

    [Fact]
    public void GetAnthropicClient_RejectsInvalidEndpoint()
    {
        var provider = new AnthropicClientProvider();
        var runtime = Runtime(ModelProviderProtocols.Anthropic) with { EndPoint = "not a uri" };

        Assert.Throws<ArgumentException>(() => provider.GetAnthropicClient(runtime));
    }

    [Fact]
    public void ResolveMainRuntime_UsesAnthropicDefaultEndpoint()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic-main",
            ProviderPreferences = new() { ["anthropic-main"] = new ModelPreference { Model = "claude-sonnet-4-5"  } },
            Providers =
            {
                ["anthropic-main"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = ModelProviderProtocols.Anthropic,
                    ApiKey = "sk-ant-test"
                }
            }
        };

        var runtime = ModelProviderResolver.ResolveMain(config);

        Assert.Equal(ModelProviderDefaults.DefaultAnthropicEndpoint, runtime.EndPoint);
        Assert.Equal(600, runtime.NetworkTimeoutSeconds);
    }

    [Fact]
    public void AnthropicCapabilities_EnablePromptCacheRequestShaping()
    {
        var capabilities = ModelProviderCapabilities.ForProtocol(ModelProviderProtocols.Anthropic);

        Assert.True(capabilities.PromptCacheRequestShaping);
        Assert.True(capabilities.NativeDeferredToolLoading);
    }

    private static EffectiveModelRuntime Runtime(string protocol, int networkTimeoutSeconds = 600) => new(
        ProviderId: protocol,
        Model: "model-a",
        Protocol: protocol,
        DisplayName: protocol,
        ApiKey: "sk-test",
        EndPoint: protocol == ModelProviderProtocols.Anthropic
            ? "https://api.anthropic.com"
            : "https://example.test/v1",
        NetworkTimeoutSeconds: networkTimeoutSeconds,
        MaxOutputTokens: null,
        IsImplicit: false,
        ModelProviderCapabilities.ForProtocol(protocol));
}
