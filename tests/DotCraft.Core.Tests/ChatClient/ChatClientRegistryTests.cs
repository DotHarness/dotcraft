using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

public sealed class ChatClientRegistryTests
{
    [Fact]
    public void ResolveSubAgentModel_ProviderSpecificSubAgentModelWins()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "main-model");
        config.SubAgent.ProviderPreferences[config.ProviderId] = new ModelPreference { Model = "sub-model" };
        var registry = new ChatClientRegistry();

        var effective = registry.ResolveSubAgentModel(config, config.ProviderId, "thread-model");

        Assert.Equal("sub-model", effective);
    }

    [Fact]
    public void ResolveSubAgentModel_EmptySubAgentModelFollowsThreadModel()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "main-model");
        config.SubAgent = new AppConfig.SubAgentConfig();
        var registry = new ChatClientRegistry();

        var main = registry.ResolveMainModel(config, "thread-model");
        var subAgent = registry.ResolveSubAgentModel(config, config.ProviderId, main);

        Assert.Equal("thread-model", subAgent);
    }

    [Fact]
    public void ResolveMainModel_EmptyThreadModelUsesProviderPreference()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "workspace-model");
        var registry = new ChatClientRegistry();

        var main = registry.ResolveMainModel(config, " ");

        Assert.Equal("workspace-model", main);
    }

    [Fact]
    public void ResolveMainModel_UsesProviderSpecificWorkspacePreference()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "initial-model");
        config.ProviderPreferences[config.ProviderId] = new ModelPreference { Model = "remembered-model" };
        var registry = new ChatClientRegistry();

        Assert.Equal("remembered-model", registry.ResolveMainModel(config));
        Assert.Equal("thread-model", registry.ResolveMainModel(config, "thread-model"));
    }

    [Fact]
    public void ResolveSubAgentRuntime_MissingProviderPreference_InheritsThreadModel()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "workspace-model");
        config.SubAgent.ProviderPreferences["another-provider"] = new ModelPreference { Model = "wrong-model" };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveSubAgentRuntime(config, config.ProviderId, "thread-model");

        Assert.Equal(config.ProviderId, runtime.ProviderId);
        Assert.Equal("thread-model", runtime.Model);
    }

    [Fact]
    public void ResolveMainRuntime_UsesExplicitAnthropicProvider()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic-main",
            ProviderPreferences = new() { ["anthropic-main"] = new ModelPreference { Model = "claude-sonnet-4-5"  } },
            Providers =
            {
                ["anthropic-main"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "Anthropic",
                    Protocol = "anthropic",
                    ApiKey = "sk-ant-test"
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal("anthropic-main", runtime.ProviderId);
        Assert.Equal("anthropic", runtime.Protocol);
        Assert.Equal("claude-sonnet-4-5", runtime.Model);
        Assert.Equal("https://api.anthropic.com", runtime.EndPoint);
        Assert.True(runtime.Capabilities.ToolCalling);
    }

    [Fact]
    public void ResolveMainRuntime_UsesProviderMaxOutputTokens()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic-main",
            ProviderPreferences = new() { ["anthropic-main"] = new ModelPreference { Model = "claude-sonnet-4-5"  } },
            Providers =
            {
                ["anthropic-main"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = "anthropic",
                    ApiKey = "sk-ant-test",
                    MaxOutputTokens = 4096
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal(4096, runtime.MaxOutputTokens);
    }

    [Fact]
    public void ResolveMainRuntime_UsesProviderStreamRetryConfig()
    {
        var config = new AppConfig
        {
            ProviderId = "openai-main",
            ProviderPreferences = new() { ["openai-main"] = new ModelPreference { Model = "gpt-5"  } },
            Providers =
            {
                ["openai-main"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = ModelProviderProtocols.OpenAIResponses,
                    ApiKey = "sk-test",
                    StreamMaxRetries = 3,
                    StreamIdleTimeoutMs = 12_000
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal(3, runtime.StreamMaxRetries);
        Assert.Equal(12_000, runtime.StreamIdleTimeoutMs);
    }

    [Fact]
    public void GetChatClient_AnthropicNormalizesMissingMaxOutputTokensToDefault()
    {
        var registry = new ChatClientRegistry();
        var missingRuntime = Runtime(ModelProviderProtocols.Anthropic, networkTimeoutSeconds: 600);
        var explicitDefaultRuntime = missingRuntime with { MaxOutputTokens = AnthropicClientProvider.DefaultMaxOutputTokens };
        var invalidRuntime = missingRuntime with { MaxOutputTokens = 0 };
        var customRuntime = missingRuntime with { MaxOutputTokens = 4096 };

        var missing = registry.GetChatClient(missingRuntime);
        var explicitDefault = registry.GetChatClient(explicitDefaultRuntime);
        var invalid = registry.GetChatClient(invalidRuntime);
        var custom = registry.GetChatClient(customRuntime);

        Assert.Same(missing, explicitDefault);
        Assert.Same(missing, invalid);
        Assert.NotSame(missing, custom);
    }

    [Fact]
    public void GetChatClient_OpenAIDoesNotAddDefaultMaxOutputTokens()
    {
        var registry = new ChatClientRegistry();
        var missingRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var explicitAnthropicDefaultRuntime = missingRuntime with { MaxOutputTokens = AnthropicClientProvider.DefaultMaxOutputTokens };

        var missing = registry.GetChatClient(missingRuntime);
        var explicitDefault = registry.GetChatClient(explicitAnthropicDefaultRuntime);

        Assert.NotSame(missing, explicitDefault);
    }

    [Fact]
    public void ResolveMainRuntime_UsesExplicitProviderEndpointAndTimeout()
    {
        var config = new AppConfig
        {
            ProviderId = "openrouter",
            ProviderPreferences = new() { ["openrouter"] = new ModelPreference { Model = "openrouter-model"  } },
            NetworkTimeoutSeconds = 600,
            Providers =
            {
                ["openrouter"] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "OpenRouter",
                    Protocol = "openai",
                    ApiKey = "sk-router-test",
                    EndPoint = "https://openrouter.ai/api/v1",
                    NetworkTimeoutSeconds = 120
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal("openrouter", runtime.ProviderId);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, runtime.Protocol);
        Assert.Equal("openrouter-model", runtime.Model);
        Assert.Equal("https://openrouter.ai/api/v1", runtime.EndPoint);
        Assert.Equal(120, runtime.NetworkTimeoutSeconds);
        Assert.False(runtime.IsImplicit);
    }

    [Fact]
    public void ResolveMainRuntime_NoProviderThrowsProviderNotConfigured()
    {
        var config = new AppConfig();
        var registry = new ChatClientRegistry();

        var exception = Assert.Throws<ModelProviderConfigurationException>(
            () => registry.ResolveMainRuntime(config));

        Assert.Equal(ModelCatalogErrorCode.ProviderNotConfigured, exception.ErrorCode);
        Assert.Equal(ModelProviderResolver.MissingProviderMessage, exception.Message);
    }

    [Fact]
    public void ResolveMainRuntime_ProviderWithoutRememberedModelThrows()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = "openai",
                    ApiKey = "sk-test"
                }
            }
        };
        var registry = new ChatClientRegistry();

        var exception = Assert.Throws<ArgumentException>(() => registry.ResolveMainRuntime(config));

        Assert.Equal("config", exception.ParamName);
        Assert.StartsWith("Model must be configured.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveMainRuntime_OpenAIProviderIdUsesExplicitProvider()
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "model-a"  } },
            Providers =
            {
                ["openai"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = "openai",
                    ApiKey = "sk-openai-test",
                    EndPoint = "https://example.test/v1"
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal("openai", runtime.ProviderId);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, runtime.Protocol);
        Assert.Equal("sk-openai-test", runtime.ApiKey);
        Assert.Equal("https://example.test/v1", runtime.EndPoint);
        Assert.False(runtime.IsImplicit);
    }

    [Fact]
    public void ResolveSubAgentRuntime_InheritsProviderAndOverridesModel()
    {
        var config = new AppConfig
        {
            ProviderId = "anthropic-main",
            ProviderPreferences = new() { ["anthropic-main"] = new ModelPreference { Model = "main-model"  } },
            SubAgent = new AppConfig.SubAgentConfig
            {
                ProviderPreferences = new Dictionary<string, ModelPreference>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anthropic-main"] = new ModelPreference { Model = "sub-model" }
                }
            },
            Providers =
            {
                ["anthropic-main"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = "anthropic",
                    ApiKey = "sk-ant-test"
                }
            }
        };
        var registry = new ChatClientRegistry();
        var main = registry.ResolveMainRuntime(config);

        var subAgent = registry.ResolveSubAgentRuntime(config, main.ProviderId, main.Model);

        Assert.Equal("anthropic-main", subAgent.ProviderId);
        Assert.Equal("anthropic", subAgent.Protocol);
        Assert.Equal("sub-model", subAgent.Model);
    }

    [Fact]
    public void ResolveConsolidationModel_EmptyConsolidationModelFallsBackToWorkspaceModel()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "workspace-model");
        config.ConsolidationModel = "";
        var registry = new ChatClientRegistry();

        var consolidation = registry.ResolveConsolidationModel(config);

        Assert.Equal("workspace-model", consolidation);
    }

    [Fact]
    public void ResolveConsolidationRuntime_UsesThreadProviderAndConsolidationModel()
    {
        var config = new AppConfig
        {
            ProviderId = "test",
            ProviderPreferences = new() { ["test"] = new ModelPreference { Model = "workspace-model"  } },
            ConsolidationModel = "memory-model",
            Providers =
            {
                ["anthropic-main"] = new AppConfig.ModelProviderConfig
                {
                    Protocol = "anthropic",
                    ApiKey = "sk-ant-test"
                }
            }
        };
        var registry = new ChatClientRegistry();

        var consolidation = registry.ResolveConsolidationRuntime(config, "anthropic-main", "thread-model");

        Assert.Equal("anthropic-main", consolidation.ProviderId);
        Assert.Equal("anthropic", consolidation.Protocol);
        Assert.Equal("memory-model", consolidation.Model);
    }

    [Fact]
    public void GetChatClient_CacheKeyIncludesNetworkTimeout()
    {
        var registry = new ChatClientRegistry();
        var firstRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var sameRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var differentTimeoutRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 900);

        var first = registry.GetChatClient(firstRuntime);
        var same = registry.GetChatClient(sameRuntime);
        var differentTimeout = registry.GetChatClient(differentTimeoutRuntime);

        Assert.Same(first, same);
        Assert.NotSame(first, differentTimeout);
    }

    [Fact]
    public void GetChatClient_CacheKeyIncludesStreamRetryConfig()
    {
        var registry = new ChatClientRegistry();
        var firstRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var sameRuntime = firstRuntime with
        {
            StreamMaxRetries = ModelProviderDefaults.DefaultStreamMaxRetries,
            StreamIdleTimeoutMs = ModelProviderDefaults.DefaultStreamIdleTimeoutMs
        };
        var differentRetriesRuntime = firstRuntime with { StreamMaxRetries = 1 };
        var differentIdleRuntime = firstRuntime with { StreamIdleTimeoutMs = 10_000 };

        var first = registry.GetChatClient(firstRuntime);
        var same = registry.GetChatClient(sameRuntime);
        var differentRetries = registry.GetChatClient(differentRetriesRuntime);
        var differentIdle = registry.GetChatClient(differentIdleRuntime);

        Assert.Same(first, same);
        Assert.NotSame(first, differentRetries);
        Assert.NotSame(first, differentIdle);
    }

    [Fact]
    public void GetChatClient_OpenAIResponsesUsesToolSearchBridge()
    {
        var registry = new ChatClientRegistry();

        var client = registry.GetChatClient(Runtime(ModelProviderProtocols.OpenAIResponses, networkTimeoutSeconds: 600));

        Assert.IsType<StreamRetryingChatClient>(client);
        Assert.IsType<OpenAIResponsesToolSearchChatClient>(
            client.GetService(typeof(OpenAIResponsesToolSearchChatClient)));
    }

    [Fact]
    public void AppConfig_DefaultNetworkTimeoutSeconds_IsTenMinutes()
    {
        var config = new AppConfig();

        Assert.Equal(600, config.NetworkTimeoutSeconds);
    }

    private static EffectiveModelRuntime Runtime(string protocol, int networkTimeoutSeconds) => new(
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
