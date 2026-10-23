using DotCraft.Agents;
using DotCraft.Configuration;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class ChatClientRegistryTests
{
    [Fact]
    public void ResolveMainModel_ThreadModelWinsOverProviderSpecificWorkspacePreference()
    {
        var config = AppConfigTestFactory.CreateOpenAI(model: "initial-model");
        config.ProviderPreferences[config.ProviderId] = new ModelPreference { Model = "remembered-model" };
        var registry = new ChatClientRegistry();

        Assert.Equal("remembered-model", registry.ResolveMainModel(config));
        Assert.Equal("remembered-model", registry.ResolveMainModel(config, " "));
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
                    ApiKey = "sk-ant-test",
                    MaxOutputTokens = 4096
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal("anthropic-main", runtime.ProviderId);
        Assert.Equal("anthropic", runtime.Protocol);
        Assert.Equal("claude-sonnet-4-5", runtime.Model);
        Assert.Equal("https://api.anthropic.com", runtime.EndPoint);
        Assert.Equal(4096, runtime.MaxOutputTokens);
        Assert.True(runtime.Capabilities.ToolCalling);
    }

    [Fact]
    public void GetChatClient_OnlyAnthropicNormalizesMissingMaxOutputTokensToDefault()
    {
        var registry = TestModelProviderRegistry.Create();
        var missingRuntime = Runtime(ModelProviderProtocols.Anthropic, networkTimeoutSeconds: 600);

        var missing = registry.GetChatClient(missingRuntime);

        Assert.Same(missing, registry.GetChatClient(missingRuntime with { MaxOutputTokens = AnthropicClientProvider.DefaultMaxOutputTokens }));
        Assert.Same(missing, registry.GetChatClient(missingRuntime with { MaxOutputTokens = 0 }));
        Assert.NotSame(missing, registry.GetChatClient(missingRuntime with { MaxOutputTokens = 4096 }));

        var openAIRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        Assert.NotSame(
            registry.GetChatClient(openAIRuntime),
            registry.GetChatClient(openAIRuntime with { MaxOutputTokens = AnthropicClientProvider.DefaultMaxOutputTokens }));
    }

    [Fact]
    public void ResolveMainRuntime_UsesExplicitProviderEndpointTimeoutAndStreamRetryConfig()
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
                    NetworkTimeoutSeconds = 120,
                    StreamMaxRetries = 3,
                    StreamIdleTimeoutMs = 12_000
                }
            }
        };
        var registry = new ChatClientRegistry();

        var runtime = registry.ResolveMainRuntime(config);

        Assert.Equal("openrouter", runtime.ProviderId);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, runtime.Protocol);
        Assert.Equal("openrouter-model", runtime.Model);
        Assert.Equal("sk-router-test", runtime.ApiKey);
        Assert.Equal("https://openrouter.ai/api/v1", runtime.EndPoint);
        Assert.Equal(120, runtime.NetworkTimeoutSeconds);
        Assert.Equal(3, runtime.StreamMaxRetries);
        Assert.Equal(12_000, runtime.StreamIdleTimeoutMs);
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
    public void GetChatClient_CacheKeyIncludesNetworkTimeoutAndStreamRetryConfig()
    {
        var registry = TestModelProviderRegistry.Create();
        var firstRuntime = Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600);
        var sameRuntime = firstRuntime with
        {
            StreamMaxRetries = ModelProviderDefaults.DefaultStreamMaxRetries,
            StreamIdleTimeoutMs = ModelProviderDefaults.DefaultStreamIdleTimeoutMs
        };

        var first = registry.GetChatClient(firstRuntime);

        Assert.Same(first, registry.GetChatClient(Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 600)));
        Assert.Same(first, registry.GetChatClient(sameRuntime));
        Assert.NotSame(first, registry.GetChatClient(Runtime(ModelProviderProtocols.OpenAI, networkTimeoutSeconds: 900)));
        Assert.NotSame(first, registry.GetChatClient(firstRuntime with { StreamMaxRetries = 1 }));
        Assert.NotSame(first, registry.GetChatClient(firstRuntime with { StreamIdleTimeoutMs = 10_000 }));
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
            Capabilities: capabilities,
            StreamMaxRetries: ModelProviderDefaults.MaxStreamMaxRetries + 1,
            StreamIdleTimeoutMs: 0,
            AuthMethod: " CHATGPTOAUTH ",
            AccountId: " account-a ",
            SupportsImageGeneration: true,
            UseResponsesLite: true,
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
        Assert.Same(capabilities, received.Capabilities);
        Assert.Equal(ModelProviderDefaults.MaxStreamMaxRetries, received.StreamMaxRetries);
        Assert.Equal(1, received.StreamIdleTimeoutMs);
        Assert.Equal(ModelProviderAuthMethods.ChatGptOAuth, received.AuthMethod);
        Assert.Equal("account-a", received.AccountId);
        Assert.True(received.SupportsImageGeneration);
        Assert.True(received.UseResponsesLite);
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
        var differentImageSupport = registry.GetChatClient(runtime with
        {
            SupportsImageGeneration = !runtime.SupportsImageGeneration
        });
        Assert.Same(first, same);
        Assert.NotSame(first, differentCapabilities);
        Assert.NotSame(first, differentImageSupport);
        Assert.Equal(3, provider.ReceivedRuntimes.Count);
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
