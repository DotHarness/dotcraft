using DotCraft.Configuration;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Auth.OpenAI;

public sealed class ModelProviderResolverOAuthTests
{
    [Fact]
    public void OAuthProviderForcesChatGptBackendEndpointAndResponsesProtocol()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-5-codex"  } },
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenAI",
                    Protocol = ModelProviderProtocols.OpenAIChatCompletions,
                    AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
                    ApiKey = "ignored",
                    EndPoint = "https://example.com/should-be-ignored"
                }
            }
        };

        var runtime = ModelProviderResolver.ResolveMain(config);
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, runtime.AuthMethod);
        Assert.True(runtime.IsChatGptOAuth);
        Assert.Equal(ModelProviderDefaults.ChatGptBackendEndpoint, runtime.EndPoint);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, runtime.Protocol);
        Assert.Null(runtime.AccountId);
        Assert.Equal(string.Empty, runtime.ApiKey);
    }

    [Fact]
    public void OAuthOnAnthropicProtocolIsRejected()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic",
            ProviderPreferences = new() { ["anthropic"] = new ModelPreference { Model = "claude-3-5-sonnet"  } },
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
}
