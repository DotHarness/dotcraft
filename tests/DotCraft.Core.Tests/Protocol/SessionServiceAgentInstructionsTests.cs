using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceAgentInstructionsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-session-agents-{Guid.NewGuid():N}");
    private readonly string _workspace;
    private readonly SessionPersistenceService _persistence;

    public SessionServiceAgentInstructionsTests()
    {
        _workspace = Directory.CreateDirectory(Path.Combine(_root, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_workspace, ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "user", ".craft"));
        _persistence = new SessionPersistenceService(new ThreadStore(Path.Combine(_root, "data")));
    }

    [Fact]
    public async Task Sources_StayStableUntilColdResumeThenReload()
    {
        var defaultPath = Path.Combine(_workspace, "AGENTS.md");
        var overridePath = Path.Combine(_workspace, "AGENTS.override.md");
        File.WriteAllText(defaultPath, "default rules");

        string threadId;
        await using (var factory = CreateAgentFactory())
        {
            var service = CreateService(factory);
            var thread = await service.CreateThreadAsync(MakeIdentity());
            threadId = thread.Id;

            Assert.Equal([defaultPath], await service.GetInstructionSourcesAsync(thread.Id));
            File.WriteAllText(overridePath, "override rules");
            Assert.Equal([defaultPath], await service.GetInstructionSourcesAsync(thread.Id));
        }

        await using (var coldFactory = CreateAgentFactory())
        {
            var coldService = CreateService(coldFactory);
            await coldService.ResumeThreadAsync(threadId);

            Assert.Equal([overridePath], await coldService.GetInstructionSourcesAsync(threadId));
        }
    }

    [Fact]
    public async Task Fork_UsesChildCwdWithoutChangingParentSnapshot()
    {
        var rootPath = Path.Combine(_workspace, "AGENTS.md");
        File.WriteAllText(rootPath, "root rules");
        var childCwd = Directory.CreateDirectory(Path.Combine(_workspace, "src", "feature")).FullName;
        var nestedPath = Path.Combine(_workspace, "src", "AGENTS.md");
        File.WriteAllText(nestedPath, "nested rules");

        await using var factory = CreateAgentFactory();
        var service = CreateService(factory);
        var parent = await service.CreateThreadAsync(MakeIdentity());
        Assert.Equal([rootPath], await service.GetInstructionSourcesAsync(parent.Id));

        var child = await service.ForkThreadAsync(parent.Id, new ThreadForkOptions { Cwd = childCwd });

        Assert.Equal([rootPath, nestedPath], await service.GetInstructionSourcesAsync(child.Id));
        Assert.Equal([rootPath], await service.GetInstructionSourcesAsync(parent.Id));
    }

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAI)]
    [InlineData(ModelProviderProtocols.OpenAIResponses)]
    [InlineData(ModelProviderProtocols.Anthropic)]
    public async Task ProviderRequest_UsesExactlyOnePlainUserInstructionItem(string protocol)
    {
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "wire project rules");
        var recorder = new RecordingChatClient();
        await using var factory = CreateAgentFactory(protocol);
        var service = new SessionService(
            factory,
            recorder.AsAIAgent(),
            _persistence,
            new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var instructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Equal("user", instructions.Role.Value);
        Assert.Contains("wire project rules", instructions.Text);
        Assert.DoesNotContain("<system-reminder>", instructions.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            recorder.LastMessages,
            message => message.Role.Value == "developer"
                       && message.Text.Contains("wire project rules", StringComparison.Ordinal));
        Assert.DoesNotContain("wire project rules", recorder.LastOptions?.Instructions ?? string.Empty);
    }

    private SessionService CreateService(AgentFactory factory) =>
        new(factory, factory.CreateAgentForMode(AgentMode.Agent), _persistence, new SessionGate());

    private AgentFactory CreateAgentFactory(string protocol = ModelProviderProtocols.OpenAI)
    {
        var config = protocol == ModelProviderProtocols.Anthropic
            ? AppConfigTestFactory.CreateAnthropic()
            : AppConfigTestFactory.CreateOpenAI();
        config.Providers[config.ProviderId].Protocol = protocol;
        config.GlobalConfigPath = Path.Combine(_root, "user", ".craft", "config.json");
        return new AgentFactory(
            dotcraftPath: Path.Combine(_root, "craft"),
            workspacePath: _workspace,
            config: config,
            memoryStore: new MemoryStore(Path.Combine(_root, "craft")),
            skillsLoader: new SkillsLoader(Path.Combine(_root, "craft")),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: Array.Empty<IToolSource>());
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "user",
        WorkspacePath = _workspace
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public ChatOptions? LastOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            LastOptions = options;
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            LastOptions = options;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test-only files.
        }
    }
}
