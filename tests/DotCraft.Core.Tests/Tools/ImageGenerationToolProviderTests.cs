using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class ProviderHostedCapabilityPlannerTests : IDisposable
{
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        foreach (var tempRoot in _tempRoots)
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ModelsImageGenerationOutsideLocalToolRegistry()
    {
        Assert.True(ProviderHostedCapabilityPlanner.Build(CreateContext(CreateOpenAIConfig())).ImageGenerationEnabled);
        Assert.True(ProviderHostedCapabilityPlanner.Build(CreateContext(CreateChatGptOAuthConfig())).ImageGenerationEnabled);
    }

    [Fact]
    public void ShouldEnableHostedImageGeneration_GatesByProviderAndConfig()
    {
        Assert.True(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(CreateOpenAIConfig())));
        Assert.True(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(CreateChatGptOAuthConfig())));

        var customEndpoint = CreateOpenAIConfig(endpoint: "https://openai-compatible.example/v1");
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(customEndpoint)));

        var optedInCustomEndpoint = CreateOpenAIConfig(
            endpoint: "https://openai-compatible.example/v1",
            supportsHostedImageGeneration: true);
        Assert.True(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(optedInCustomEndpoint)));

        var disabledOfficial = CreateOpenAIConfig(supportsHostedImageGeneration: false);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(disabledOfficial)));

        var disabledOAuth = CreateChatGptOAuthConfig(supportsHostedImageGeneration: false);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(disabledOAuth)));

        var missingApiKey = CreateOpenAIConfig(apiKey: string.Empty);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(missingApiKey)));

        var optedInMissingApiKey = CreateOpenAIConfig(apiKey: string.Empty, supportsHostedImageGeneration: true);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(optedInMissingApiKey)));

        var unsupportedAuth = CreateOpenAIConfig(authMethod: "customOAuth", supportsHostedImageGeneration: true);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(unsupportedAuth)));

        var chatCompletions = CreateOpenAIConfig(protocol: ModelProviderProtocols.OpenAIChatCompletions);
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(chatCompletions)));

        var disabled = CreateOpenAIConfig();
        disabled.Tools.ImageGeneration.Enabled = false;
        Assert.False(ProviderHostedCapabilityPlanner.ShouldEnableHostedImageGeneration(CreateContext(disabled)));
    }

    [Fact]
    public void Build_FreezesDeferredSearchModeAndProviderProtocol()
    {
        var native = ProviderHostedCapabilityPlanner.Build(CreateContext(CreateOpenAIConfig()));
        Assert.Equal(DeferredToolLoadingMode.Native, native.DeferredToolSearch?.Mode);
        Assert.Equal(ModelProviderProtocols.OpenAIResponses, native.DeferredToolSearch?.ProviderProtocol);

        var simulated = ProviderHostedCapabilityPlanner.Build(CreateContext(
            CreateOpenAIConfig(protocol: ModelProviderProtocols.OpenAIChatCompletions)));
        Assert.Equal(DeferredToolLoadingMode.Simulated, simulated.DeferredToolSearch?.Mode);
        Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, simulated.DeferredToolSearch?.ProviderProtocol);
    }

    private AgentRuntimeContext CreateContext(AppConfig config)
    {
        var root = CreateTempRoot();
        var botPath = Path.Combine(root, ".craft");
        Directory.CreateDirectory(botPath);
        return new AgentRuntimeContext
        {
            Config = config,
            ChatClient = new NoOpChatClient(),
            ChatClientRegistry = TestModelProviderRegistry.Create(),
            WorkspacePath = root,
            BotPath = botPath,
            MemoryStore = new MemoryStore(botPath),
            SkillsLoader = new SkillsLoader(botPath),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([])
        };
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-imagegen-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempRoots.Add(root);
        return root;
    }

    private static AppConfig CreateOpenAIConfig(
        string? endpoint = null,
        string protocol = ModelProviderProtocols.OpenAIResponses,
        string apiKey = "sk-test",
        bool? supportsHostedImageGeneration = null,
        string authMethod = ModelProviderAuthMethods.ApiKey)
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            ProviderPreferences = new() { ["openai"] = new ModelPreference { Model = "gpt-5"  } }
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            Protocol = protocol,
            ApiKey = apiKey,
            EndPoint = endpoint ?? string.Empty,
            AuthMethod = authMethod,
            SupportsHostedImageGeneration = supportsHostedImageGeneration
        };
        return config;
    }

    private static AppConfig CreateChatGptOAuthConfig(bool? supportsHostedImageGeneration = null)
    {
        var config = new AppConfig
        {
            ProviderId = "chatgpt",
            ProviderPreferences = new() { ["chatgpt"] = new ModelPreference { Model = "gpt-5"  } }
        };
        config.Providers["chatgpt"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth,
            SupportsHostedImageGeneration = supportsHostedImageGeneration
        };
        return config;
    }

    private sealed class NoOpChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
