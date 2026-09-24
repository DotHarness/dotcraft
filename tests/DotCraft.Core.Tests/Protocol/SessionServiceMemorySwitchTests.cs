using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMemorySwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-memory-switch-{Guid.NewGuid():N}");
    private readonly AppConfig _config = AppConfigTestFactory.CreateOpenAI();
    private readonly RecordingChatClient _recorder = new();
    private readonly MemoryStore _memoryStore;

    public SessionServiceMemorySwitchTests()
    {
        Directory.CreateDirectory(_root);
        _memoryStore = new MemoryStore(_root);
        File.WriteAllText(_memoryStore.LongTermFilePath, "remembered-lesson");
        new DreamStore(_root).SaveDreamRun("# Dream Memory\n\n- inferred-context", null);
    }

    [Fact]
    public async Task NewThreads_CaptureTheSwitch_AndOnlyMemoryThreadsCarryMemoryOrDreams()
    {
        await using var factory = CreateAgentFactory();
        var service = CreateService(factory);

        var withMemory = await service.CreateThreadAsync(MakeIdentity());
        _config.Memory.Enabled = false;
        var withoutMemory = await service.CreateThreadAsync(MakeIdentity());

        Assert.True(withMemory.Configuration?.MemoryEnabled);
        Assert.False(withoutMemory.Configuration?.MemoryEnabled);

        await DrainAsync(service.SubmitInputAsync(withoutMemory.Id, [new TextContent("hello")]));
        Assert.DoesNotContain("remembered-lesson", _recorder.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("inferred-context", _recorder.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetFullPath(_memoryStore.MemoryDirectoryPath), _recorder.LastPrompt, StringComparison.Ordinal);

        await DrainAsync(service.SubmitInputAsync(withMemory.Id, [new TextContent("hello")]));
        Assert.Contains("remembered-lesson", _recorder.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("inferred-context", _recorder.LastPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateThreadConfiguration_KeepsTheCapturedSwitch()
    {
        _config.Memory.Enabled = false;
        await using var factory = CreateAgentFactory();
        var service = CreateService(factory);
        var thread = await service.CreateThreadAsync(MakeIdentity());

        await service.UpdateThreadConfigurationAsync(thread.Id, new ThreadConfiguration { Mode = "agent" });

        var reloaded = await service.GetThreadAsync(thread.Id);
        Assert.False(reloaded.Configuration?.MemoryEnabled);
    }

    [Fact]
    public async Task ForkWithPartialConfig_InheritsTheSourceMemorySettings()
    {
        _config.Memory.Enabled = false;
        await using var factory = CreateAgentFactory();
        var service = CreateService(factory);
        var source = await service.CreateThreadAsync(MakeIdentity(), new ThreadConfiguration { MemoryScope = "nightly" });
        await DrainAsync(service.SubmitInputAsync(source.Id, [new TextContent("hello")]));
        _config.Memory.Enabled = true;

        var fork = await service.ForkThreadAsync(
            source.Id,
            new ThreadForkOptions { Config = new ThreadConfiguration { Mode = "agent" } });

        Assert.False(fork.Configuration?.MemoryEnabled);
        Assert.Equal("nightly", fork.Configuration?.MemoryScope);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private SessionService CreateService(AgentFactory factory) =>
        new(
            factory,
            factory.CreateAgentForMode(AgentMode.Agent),
            new SessionPersistenceService(new ThreadStore(Path.Combine(_root, "data"))),
            new SessionGate());

    private AgentFactory CreateAgentFactory() =>
        new(
            dotcraftPath: _root,
            workspacePath: _root,
            config: _config,
            memoryStore: _memoryStore,
            skillsLoader: new SkillsLoader(_root),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            chatClient: _recorder,
            toolSources: Array.Empty<IToolSource>());

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "user",
        WorkspacePath = _root
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        public string LastPrompt { get; private set; } = string.Empty;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Record(chatMessages, options);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Record(chatMessages, options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        private void Record(IEnumerable<ChatMessage> chatMessages, ChatOptions? options) =>
            LastPrompt = (options?.Instructions ?? string.Empty) + "\n" + string.Join("\n", chatMessages.Select(message => message.Text));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
