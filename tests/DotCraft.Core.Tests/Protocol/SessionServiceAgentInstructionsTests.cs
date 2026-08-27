using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
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

    [Fact]
    public async Task InstructionSources_RecordEffectiveSnapshotAndEmptyColdResume()
    {
        var agentsPath = Path.Combine(_workspace, "AGENTS.md");
        File.WriteAllText(agentsPath, "trace project rules");
        var traceStore = new TraceStore();
        var collector = new TraceCollector(traceStore);

        string threadId;
        await using (var factory = CreateAgentFactory())
        {
            var service = CreateService(factory, collector);
            var thread = await service.CreateThreadAsync(MakeIdentity());
            threadId = thread.Id;

            Assert.Equal([agentsPath], await service.GetInstructionSourcesAsync(thread.Id));
            Assert.Equal([agentsPath], await service.GetInstructionSourcesAsync(thread.Id));

            var trace = Assert.Single(
                traceStore.GetEvents(thread.Id),
                evt => evt.Type == TraceEventType.AgentInstructions);
            Assert.Contains("trace project rules", trace.Content, StringComparison.Ordinal);
            using var metadata = JsonDocument.Parse(trace.MetadataJson!);
            Assert.Equal(1, metadata.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("agents_md.instructions", metadata.RootElement.GetProperty("kind").GetString());
            Assert.Equal("user", metadata.RootElement.GetProperty("role").GetString());
            Assert.Equal(
                [agentsPath],
                metadata.RootElement.GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
        }

        File.Delete(agentsPath);
        await using (var coldFactory = CreateAgentFactory())
        {
            var coldService = CreateService(coldFactory, collector);
            await coldService.ResumeThreadAsync(threadId);

            Assert.Empty(await coldService.GetInstructionSourcesAsync(threadId));
        }

        var traces = traceStore.GetEvents(threadId)
            .Where(evt => evt.Type == TraceEventType.AgentInstructions)
            .ToList();
        Assert.Equal(2, traces.Count);
        Assert.Equal(string.Empty, traces[^1].Content);
        using var emptyMetadata = JsonDocument.Parse(traces[^1].MetadataJson!);
        Assert.Empty(emptyMetadata.RootElement.GetProperty("sources").EnumerateArray());
    }

    [Theory]
    [InlineData(ModelProviderProtocols.OpenAI)]
    [InlineData(ModelProviderProtocols.OpenAIResponses)]
    [InlineData(ModelProviderProtocols.Anthropic)]
    public async Task ProviderRequest_UsesExactlyOnePlainUserInstructionItem(string protocol)
    {
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "wire project rules");
        var recorder = new RecordingChatClient();
        var traceStore = new TraceStore();
        var collector = new TraceCollector(traceStore);
        await using var factory = CreateAgentFactory(protocol);
        var service = new SessionService(
            factory,
            recorder.AsAIAgent(),
            _persistence,
            new SessionGate(),
            traceCollector: collector);
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
        var trace = Assert.Single(
            traceStore.GetEvents(thread.Id),
            evt => evt.Type == TraceEventType.AgentInstructions);
        Assert.Equal(instructions.Text, trace.Content);
    }

    [Fact]
    public async Task ActiveTurn_UsesWorkspaceCapturedAtAdmission()
    {
        File.WriteAllText(Path.Combine(_workspace, "AGENTS.md"), "admitted workspace rules");
        var updatedWorkspace = Directory.CreateDirectory(Path.Combine(_root, "updated-repo")).FullName;
        Directory.CreateDirectory(Path.Combine(updatedWorkspace, ".git"));
        File.WriteAllText(Path.Combine(updatedWorkspace, "AGENTS.md"), "updated workspace rules");
        var recorder = new RecordingChatClient();

        await using var factory = CreateAgentFactory();
        var service = new SessionService(
            factory,
            recorder.AsAIAgent(),
            _persistence,
            new SessionGate());
        var thread = await service.CreateThreadAsync(MakeIdentity());
        var runtime = Assert.IsType<ThreadRuntime>(service.DebugGetRuntime(thread.Id));
        runtime.AgentLock = new SemaphoreSlim(0, 1);

        var firstTurn = DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first")]));
        await WaitUntilAsync(() => runtime.Turns.Values.Any(turn => turn.Context != null));
        var admittedTurn = Assert.Single(runtime.Turns.Values);
        Assert.Equal(_workspace, admittedTurn.Context!.Workspace.Cwd);

        runtime.Thread.Configuration = ThreadWorkspaceResolver.Apply(
            runtime.Thread.WorkspacePath,
            runtime.Thread.Configuration,
            updatedWorkspace,
            [updatedWorkspace]);
        Assert.Equal(updatedWorkspace, ThreadWorkspaceResolver.Resolve(runtime.Thread).Cwd);

        runtime.AgentLock.Release();
        await firstTurn.WaitAsync(TimeSpan.FromSeconds(5));

        var firstInstructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Contains("admitted workspace rules", firstInstructions.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("updated workspace rules", firstInstructions.Text, StringComparison.Ordinal);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("second")]));

        var secondInstructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Contains("updated workspace rules", secondInstructions.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("admitted workspace rules", secondInstructions.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FullHistoryFork_InheritsFactoryOwnedStableInstructionSnapshot()
    {
        var agentsPath = Path.Combine(_workspace, "AGENTS.md");
        File.WriteAllText(agentsPath, "parent snapshot v1");
        var recorder = new RecordingChatClient();

        await using var factory = CreateAgentFactory();
        Assert.IsType<ContextPageManager>(factory.RuntimeContext.ContextPageManager);
        var service = new SessionService(
            factory,
            recorder.AsAIAgent(),
            _persistence,
            new SessionGate());
        var parent = await service.CreateThreadAsync(MakeIdentity());
        await DrainAsync(service.SubmitInputAsync(parent.Id, [new TextContent("parent request")]));
        var parentHistory = recorder.LastMessages.Select(static message => message.Clone()).ToList();

        File.WriteAllText(agentsPath, "filesystem snapshot v2");
        var child = await service.CreateThreadAsync(
            MakeIdentity(),
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                ParentTurnId = parent.Turns[^1].Id,
                RootThreadId = parent.Id,
                Depth = 1,
                AgentPath = "/root/inspect",
                TaskName = "inspect",
                RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
                ForkTurns = "all"
            }));
        child.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = child.Id,
            Status = TurnStatus.Completed,
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        });
        child.TurnSequenceHighWatermark = 1;

        var materializer = (INativeSubAgentForkMaterializationService)service;
        Assert.True(await materializer.MaterializeNativeSubAgentForkAsync(
            parent,
            child,
            parentHistory,
            CancellationToken.None));

        await DrainAsync(service.SubmitInputAsync(child.Id, [new TextContent("child request")]));

        var instructions = Assert.Single(recorder.LastMessages, AgentInstructionsHistory.IsInstructions);
        Assert.Contains("parent snapshot v1", instructions.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("filesystem snapshot v2", instructions.Text, StringComparison.Ordinal);
    }

    private SessionService CreateService(AgentFactory factory, TraceCollector? traceCollector = null) =>
        new(
            factory,
            factory.CreateAgentForMode(AgentMode.Agent),
            _persistence,
            new SessionGate(),
            traceCollector: traceCollector);

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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the admitted turn.");
            await Task.Delay(20);
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
