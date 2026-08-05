using DotCraft.Agents;
using DotCraft.Configuration;
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
    public async Task NativeSubAgent_NonResponses_KeepsGuidanceInSystemInstructions(string protocol)
    {
        var client = new RecordingChatClient();
        await using var factory = CreateFactory(protocol, client);
        var agent = factory.CreateAgentWithTools([], modeManager: null, factory.RuntimeContext);

        await DrainAsync(agent.RunStreamingAsync(
            new ChatMessage(ChatRole.User, "child task"),
            []));

        Assert.Contains(Guidance, client.LastOptions?.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain(client.LastMessages, message => message.Role.Value == "developer");
    }

    [Fact]
    public async Task NativeSubAgent_Responses_OmitsGuidanceFromSystemInstructions()
    {
        var client = new RecordingChatClient();
        await using var factory = CreateFactory(ModelProviderProtocols.OpenAIResponses, client);
        var agent = factory.CreateAgentWithTools([], modeManager: null, factory.RuntimeContext);

        await DrainAsync(agent.RunStreamingAsync(
            new ChatMessage(ChatRole.User, "child task"),
            []));

        Assert.DoesNotContain(Guidance, client.LastOptions?.Instructions ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(client.LastMessages, message => message.Role.Value == "developer");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private AgentFactory CreateFactory(string protocol, RecordingChatClient client)
    {
        const string ProviderId = "test-provider";
        const string Model = "test-model";
        var config = new AppConfig
        {
            ProviderId = ProviderId,
            ProviderPreferences =
            {
                [ProviderId] = new ModelPreference { Model = Model }
            },
            Providers =
            {
                [ProviderId] = new AppConfig.ModelProviderConfig
                {
                    DisplayName = "Test Provider",
                    Protocol = protocol,
                    ApiKey = "test-key"
                }
            }
        };
        var registry = new ChatClientRegistry();
        var memoryStore = new MemoryStore(_tempDir);
        var skillsLoader = new SkillsLoader(_tempDir);
        var runtimeContext = new AgentRuntimeContext
        {
            Config = config,
            ChatClient = client,
            ChatClientRegistry = registry,
            EffectiveProviderId = ProviderId,
            EffectiveProviderProtocol = protocol,
            EffectiveMainModel = Model,
            WorkspacePath = _tempDir,
            BotPath = _tempDir,
            MemoryStore = memoryStore,
            SkillsLoader = skillsLoader,
            ApprovalService = new AutoApproveApprovalService(),
            CurrentThreadId = "thread_child",
            CurrentThreadSource = ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                RuntimeType = NativeSubAgentRuntime.RuntimeTypeName
            }),
            PromptProfile = SubAgentPromptProfiles.Light,
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
}
