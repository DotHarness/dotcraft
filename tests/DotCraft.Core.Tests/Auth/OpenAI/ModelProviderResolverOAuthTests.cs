using DotCraft.Configuration;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class ModelProviderResolverOAuthTests
{
    [Fact]
    public void OAuthProviderForcesChatGptBackendEndpointAndResponsesProtocol()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            Model = "gpt-5-codex",
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
                    ApiKey = "ignored",
                    EndPoint = "https://example.com/should-be-ignored",
                    ChatGptAccountId = "acct_test"
                }
            }
        };

        var runtime = ModelProviderResolver.ResolveMain(config);
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, runtime.AuthMethod);
        Assert.True(runtime.IsChatGptOAuth);
        Assert.Equal(ModelProviderDefaults.ChatGptBackendEndpoint, runtime.EndPoint);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, runtime.Protocol);
        Assert.Equal("acct_test", runtime.ChatGptAccountId);
        Assert.Equal(string.Empty, runtime.ApiKey);
    }

    [Fact]
    public void ApiKeyProviderRetainsExplicitEndpointAndApiKey()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            Model = "gpt-4o",
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                    AuthMethod = ModelProviderAuthMethods.ApiKey,
                    ApiKey = "sk-test",
                    EndPoint = "https://custom.example.com/v1"
                }
            }
        };

        var runtime = ModelProviderResolver.ResolveMain(config);
        Assert.False(runtime.IsChatGptOAuth);
        Assert.Equal("sk-test", runtime.ApiKey);
        Assert.Equal("https://custom.example.com/v1", runtime.EndPoint);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, runtime.Protocol);
    }

    [Fact]
    public void OAuthOnAnthropicProtocolIsRejected()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic",
            Model = "claude-3-5-sonnet",
            Providers =
            {
                ["anthropic"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "Anthropic",
                    Protocol = ModelProviderProtocols.Anthropic,
                    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
                }
            }
        };

        Assert.Throws<ModelProviderConfigurationException>(() => ModelProviderResolver.ResolveMain(config));
    }

    [Fact]
    public void NormalizeAuthMethodAcceptsCaseInsensitiveAndDefaultsToApiKey()
    {
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, ModelProviderAuthMethods.Normalize("CHATGPTOAUTH"));
        Assert.Equal(ModelProviderAuthMethods.ApiKey, ModelProviderAuthMethods.Normalize(null));
        Assert.Equal(ModelProviderAuthMethods.ApiKey, ModelProviderAuthMethods.Normalize(""));
        Assert.Equal(ModelProviderAuthMethods.ApiKey, ModelProviderAuthMethods.Normalize("apikey"));
    }
}
