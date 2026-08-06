using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

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
        var registry = TestModelProviderRegistry.Create();
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
        var registry = TestModelProviderRegistry.Create();
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
                    Protocol = "openai-chat-completions",
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
                    Protocol = "openai-chat-completions",
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
                    Protocol = "openai-chat-completions",
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
        var registry = TestModelProviderRegistry.Create();
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
        var registry = TestModelProviderRegistry.Create();
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
    public void GetChatClient_PassesCompleteNormalizedRuntimeToProvider()
    {
        var provider = new RecordingModelProvider();
        var registry = new ChatClientRegistry(new ModelProviderRegistry([provider]));
        var capabilities = new ModelProviderCapabilities
        {
            ExtendedThinking = true,
            NativeDeferredToolLoading = true
        };
        var runtime = new EffectiveModelRuntime(
            ProviderId: "provider-a",
            Model: " model-a ",
            Protocol: " OPENAI-RESPONSES ",
            DisplayName: "Provider A",
            ApiKey: " key-a ",
            EndPoint: " https://example.test/v1 ",
            NetworkTimeoutSeconds: 0,
            MaxOutputTokens: 123,
            IsImplicit: true,
            Capabilities: capabilities,
            StreamMaxRetries: ModelProviderDefaults.MaxStreamMaxRetries + 1,
            StreamIdleTimeoutMs: 0,
            AuthMethod: " CHATGPTOAUTH ",
            ChatGptAccountId: " account-a ",
            SupportsHostedImageGeneration: true,
            UseResponsesLite: true,
            SupportsParallelToolCalls: true,
            ProviderStateDirectory: "provider-state");

        _ = registry.GetChatClient(runtime);

        var received = Assert.Single(provider.ReceivedRuntimes);
        Assert.Equal("provider-a", received.ProviderId);
        Assert.Equal("model-a", received.Model);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, received.Protocol);
        Assert.Equal("Provider A", received.DisplayName);
        Assert.Equal("key-a", received.ApiKey);
        Assert.Equal("https://example.test/v1", received.EndPoint);
        Assert.Equal(1, received.NetworkTimeoutSeconds);
        Assert.Equal(123, received.MaxOutputTokens);
        Assert.True(received.IsImplicit);
        Assert.Same(capabilities, received.Capabilities);
        Assert.Equal(ModelProviderDefaults.MaxStreamMaxRetries, received.StreamMaxRetries);
        Assert.Equal(1, received.StreamIdleTimeoutMs);
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, received.AuthMethod);
        Assert.Equal("account-a", received.ChatGptAccountId);
        Assert.True(received.SupportsHostedImageGeneration);
        Assert.True(received.UseResponsesLite);
        Assert.True(received.SupportsParallelToolCalls);
        Assert.Equal("provider-state", received.ProviderStateDirectory);
    }

    [Fact]
    public void GetChatClient_CacheKeyIncludesProviderObservableRuntimeFields()
    {
        var provider = new RecordingModelProvider();
        var registry = new ChatClientRegistry(new ModelProviderRegistry([provider]));
        var runtime = Runtime(ModelProviderProtocols.OpenAIResponses, networkTimeoutSeconds: 600);

        var first = registry.GetChatClient(runtime);
        var same = registry.GetChatClient(runtime with { });
        var differentCapabilities = registry.GetChatClient(runtime with
        {
            Capabilities = runtime.Capabilities with { ExtendedThinking = !runtime.Capabilities.ExtendedThinking }
        });
        var differentHostedImageSupport = registry.GetChatClient(runtime with
        {
            SupportsHostedImageGeneration = !runtime.SupportsHostedImageGeneration
        });
        var differentParallelToolSupport = registry.GetChatClient(runtime with
        {
            SupportsParallelToolCalls = !runtime.SupportsParallelToolCalls
        });

        Assert.Same(first, same);
        Assert.NotSame(first, differentCapabilities);
        Assert.NotSame(first, differentHostedImageSupport);
        Assert.NotSame(first, differentParallelToolSupport);
        Assert.Equal(4, provider.ReceivedRuntimes.Count);
    }

    [Fact]
    public void GetChatClient_OpenAIResponsesUsesToolSearchBridge()
    {
        var registry = TestModelProviderRegistry.Create();

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

    private sealed class RecordingModelProvider : IModelProvider
    {
        public IReadOnlyCollection<string> Protocols { get; } =
            [ModelProviderProtocols.OpenAIResponses];

        public List<EffectiveModelRuntime> ReceivedRuntimes { get; } = [];

        public IChatClient CreateChatClient(EffectiveModelRuntime runtime)
        {
            ReceivedRuntimes.Add(runtime);
            return new NoopChatClient();
        }
    }

    private sealed class NoopChatClient : IChatClient
    {
        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }
    }
}
