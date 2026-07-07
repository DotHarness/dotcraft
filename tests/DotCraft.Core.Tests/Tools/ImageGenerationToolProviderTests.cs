using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tools;

public sealed class ImageGenerationToolProviderTests : IDisposable
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
    public void CreateTools_DoesNotExposeClientImagegenTool()
    {
        Assert.Empty(new ImageGenerationToolProvider().CreateTools(CreateContext(CreateOpenAIConfig())));
        Assert.Empty(new ImageGenerationToolProvider().CreateTools(CreateContext(CreateChatGptOAuthConfig())));
    }

    [Fact]
    public void ShouldEnableHostedImageGeneration_GatesByProviderAndConfig()
    {
        Assert.True(ImageGenerationToolProvider.ShouldEnableHostedImageGeneration(CreateContext(CreateOpenAIConfig())));
        Assert.True(ImageGenerationToolProvider.ShouldEnableHostedImageGeneration(CreateContext(CreateChatGptOAuthConfig())));

        var customEndpoint = CreateOpenAIConfig(endpoint: "https://openai-compatible.example/v1");
        Assert.False(ImageGenerationToolProvider.ShouldEnableHostedImageGeneration(CreateContext(customEndpoint)));

        var chatCompletions = CreateOpenAIConfig(protocol: ModelProviderProtocols.OpenAIChatCompletions);
        Assert.False(ImageGenerationToolProvider.ShouldEnableHostedImageGeneration(CreateContext(chatCompletions)));

        var disabled = CreateOpenAIConfig();
        disabled.Tools.ImageGeneration.Enabled = false;
        Assert.False(ImageGenerationToolProvider.ShouldEnableHostedImageGeneration(CreateContext(disabled)));
    }

    private ToolProviderContext CreateContext(AppConfig config)
    {
        var root = CreateTempRoot();
        var botPath = Path.Combine(root, ".craft");
        Directory.CreateDirectory(botPath);
        return new ToolProviderContext
        {
            Config = config,
            ChatClient = new NoOpChatClient(),
            ChatClientRegistry = new ChatClientRegistry(),
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
        string protocol = ModelProviderProtocols.OpenAIResponses)
    {
        var config = new AppConfig
        {
            ProviderId = "openai",
            Model = "gpt-5"
        };
        config.Providers["openai"] = new AppConfig.ModelProviderConfig
        {
            Protocol = protocol,
            ApiKey = "sk-test",
            EndPoint = endpoint ?? string.Empty
        };
        return config;
    }

    private static AppConfig CreateChatGptOAuthConfig()
    {
        var config = new AppConfig
        {
            ProviderId = "chatgpt",
            Model = "gpt-5"
        };
        config.Providers["chatgpt"] = new AppConfig.ModelProviderConfig
        {
            Protocol = ModelProviderProtocols.OpenAIResponses,
            AuthMethod = ModelProviderAuthMethods.ChatGptOAuth
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
