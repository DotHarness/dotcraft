using DotCraft.Agents;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using Xunit;

namespace DotCraft.Tests.Agents;

public sealed class AgentFactorySubAgentGuidanceTests : IDisposable
{
    private const string Guidance = "Investigate the assigned scope carefully.";
    private readonly string _tempDir;

    public AgentFactorySubAgentGuidanceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AFSAG_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAIChatCompletions)]
    [InlineData(ModelProviderProtocols.Anthropic)]
    [InlineData(ModelProviderProtocols.OpenAIResponses)]
    public async Task NativeSubAgent_OmitsRoleGuidanceFromSystemInstructions(string protocol)
    {
        var client = new RecordingChatClient();
        await using var factory = CreateFactory(protocol, client);
        var agent = factory.CreateAgentWithTools([], modeManager: null, factory.RuntimeContext);

        await DrainAsync(agent.RunStreamingAsync(
            new ChatMessage(ChatRole.User, "child task"),
            []));

        // Role text is a thread context item on every protocol so the child's instruction channel
        // stays byte-identical to its parent's and can share the parent's cache prefix.
        Assert.DoesNotContain(Guidance, client.LastOptions?.Instructions ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(client.LastMessages, message => message.Role.Value == "developer");
    }

    [Theory]
    [InlineData("gpt-5.6-sol")]
    [InlineData("gpt-5.3-codex")]
    [InlineData("unknown-model")]
    public async Task ChatGptOAuthResponses_EnablesParallelToolCalls(string model)
    {
        var client = new RecordingChatClient();
        await using var factory = CreateFactory(
            ModelProviderProtocols.OpenAIResponses,
            client,
            model,
            ModelProviderAuthMethods.ChatGptOAuth);
        var agent = factory.CreateAgentWithTools([], modeManager: null, factory.RuntimeContext);

        await DrainAsync(agent.RunStreamingAsync(
            new ChatMessage(ChatRole.User, "child task"),
            []));

        Assert.True(client.LastOptions?.AllowMultipleToolCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AgentFactory CreateFactory(
        string protocol,
        RecordingChatClient client,
        string model = "test-model",
        string authMethod = ModelProviderAuthMethods.ApiKey)
    {
        const string ProviderId = "test-provider";
        var config = new AppConfig
        {
            ProviderId = ProviderId,
            ProviderPreferences =
            {
                [ProviderId] = new ModelPreference { Model = model }
            },
            Providers =
            {
                [ProviderId] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "Test Provider",
                    Protocol = protocol,
                    ApiKey = "test-key",
                    AuthMethod = authMethod
                }
            }
        };
        var registry = authMethod == ModelProviderAuthMethods.ChatGptOAuth
            ? new ChatClientRegistry(new OpenAIClientProvider(
                new FakeOpenAIAuthService(),
                new OpenAIInstallationIdProvider(_tempDir)))
            : TestModelProviderRegistry.Create();
        var memoryStore = new MemoryStore(_tempDir);
        var skillsLoader = new SkillsLoader(_tempDir);
        var runtimeContext = new AgentRuntimeContext
        {
            Config = config,
            ChatClient = client,
            ChatClientRegistry = registry,
            EffectiveProviderId = ProviderId,
            EffectiveProviderProtocol = protocol,
            EffectiveMainModel = model,
            WorkspacePath = _tempDir,
            BotPath = _tempDir,
            MemoryStore = memoryStore,
            SkillsLoader = skillsLoader,
            ContextPageManager = new ContextPageManager(),
            ApprovalService = new AutoApproveApprovalService(),
            CurrentThreadId = "thread_child",
            CurrentThreadSource = ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                RuntimeType = NativeSubAgentRuntime.RuntimeTypeName
            }),
            RoleInstructions = Guidance
        };

        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memoryStore,
            skillsLoader: skillsLoader,
            approvalService: runtimeContext.ApprovalService,
            blacklist: null,
            runtimeContext: runtimeContext,
            chatClientRegistry: registry,
            chatClient: client,
            toolSources: []);
    }

    private static async Task DrainAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        await foreach (var _ in updates)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];
        public ChatOptions? LastOptions { get; private set; }

        public void Dispose()
        {
        }

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Capture(messages, options);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Capture(messages, options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        private void Capture(IEnumerable<ChatMessage> messages, ChatOptions? options)
        {
            LastMessages = messages.Select(message => message.Clone()).ToList();
            LastOptions = options?.Clone();
        }
    }

    private sealed class FakeOpenAIAuthService : IOpenAIAuthService
    {
        public bool IsAuthenticated => true;
        public event Action<OpenAIAuthStatus>? LoggedIn { add { } remove { } }
        public event Action? LoggedOut { add { } remove { } }
        public OpenAIAuthStatus GetStatus() => new(
            true,
            "acct_test",
            "plus",
            "test@example.com",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1));
        public Task<OpenAIAuthStatus> LoginAsync(
            bool openBrowser,
            Action<string>? onAuthorizationUrl,
            CancellationToken cancellationToken) => Task.FromResult(GetStatus());
        public Task LogoutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken) =>
            Task.FromResult("access-token");
        public string? GetAccountId() => "acct_test";
    }
}
