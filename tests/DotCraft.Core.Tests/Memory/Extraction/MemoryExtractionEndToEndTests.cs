using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Memory.Extraction;
using DotCraft.Persistence;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tests.Tools;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Memory.Extraction;

public sealed class MemoryExtractionEndToEndTests : IDisposable
{
    private const string ExtractionMarker = "memory extraction pass";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dotcraft-extraction-{Guid.NewGuid():N}");
    private readonly string _workspace;
    private readonly string _craft;
    private readonly MemoryStore _memoryStore;

    public MemoryExtractionEndToEndTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
        _memoryStore = new MemoryStore(_craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task SeventhTurn_ForksAnExtractionThatSavesAMemoryLocallyAndLeavesTheRemoteThreadUntouched()
    {
        var memoryFile = Path.Combine(_memoryStore.MemoryDirectoryPath, "feedback_tests.md");
        var outsideFile = Path.Combine(_workspace, "README.md");
        var chat = new ScriptedChatClient(
        [
            WriteFile("call-outside", outsideFile, "Not a memory."),
            WriteFile("call-memory", memoryFile, "Run only the focused tests.")
        ]);
        var remote = new RoutedRemoteClient();
        await using var harness = await Harness.StartAsync(this, chat, remote);
        remote.Connect(harness.Thread.Id);

        for (var i = 1; i < MemoryExtractionScheduler.Cadence; i++)
            await harness.SubmitAsync($"Request {i}");
        Assert.Empty(chat.ExtractionRequests);
        await harness.SubmitAsync("From now on, run only the focused tests.");
        var turnsAfterReply = await harness.SerializeTurnsAsync();
        await harness.Scheduler.WhenIdleAsync();

        Assert.Equal("Run only the focused tests.", File.ReadAllText(memoryFile).Trim());
        Assert.False(File.Exists(outsideFile));
        var fork = Assert.Single(harness.Created);
        Assert.Equal([fork], harness.Deleted);
        Assert.Equal([fork], remote.Forked);
        Assert.Equal(0, remote.RemoteCalls);
        Assert.Equal(turnsAfterReply, await harness.SerializeTurnsAsync());
        Assert.Equal(2, chat.ExtractionRequests.Count);

        var parent = chat.ThreadRequests.Last();
        var extraction = chat.ExtractionRequests.First();
        Assert.Equal(parent.Messages, extraction.Messages.Take(parent.Messages.Count));
        Assert.Equal(parent.Instructions, extraction.Instructions);
        Assert.Equal(parent.Tools, extraction.Tools);
        Assert.Equal(harness.Thread.Id, parent.CacheKey);
        Assert.Equal(parent.CacheKey, extraction.CacheKey);

        using var outcome = harness.Outcome(fork);
        Assert.Equal("completed", outcome.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(harness.Thread.Id, outcome.RootElement.GetProperty("parentThreadId").GetString());
        Assert.Equal(
            (await harness.Service.GetThreadAsync(harness.Thread.Id)).Turns[^1].Id,
            outcome.RootElement.GetProperty("parentTurnId").GetString());
        Assert.Equal(
            ["feedback_tests.md"],
            outcome.RootElement.GetProperty("writtenFiles").EnumerateArray().Select(file => file.GetString()));
        Assert.Equal(harness.Thread.Id, harness.TraceStore.DescribeSessionRelationships([fork])[fork].ParentSessionKey);
    }

    [Fact]
    public async Task ExtractionThatKeepsCallingTools_StopsAfterFiveModelRequests()
    {
        File.WriteAllText(Path.Combine(_workspace, "notes.md"), "Notes.");
        var chat = new ScriptedChatClient(
            [new FunctionCallContent("call-read", "ReadFile", new Dictionary<string, object?> { ["path"] = "notes.md" })],
            repeatCalls: true);
        await using var harness = await Harness.StartAsync(this, chat);

        for (var i = 1; i <= MemoryExtractionScheduler.Cadence; i++)
            await harness.SubmitAsync($"Request {i}");
        await harness.Scheduler.WhenIdleAsync();

        Assert.Equal(5, chat.ExtractionRequests.Count);
        using var outcome = harness.Outcome(Assert.Single(harness.Created));
        Assert.Equal("completed", outcome.RootElement.GetProperty("outcome").GetString());
    }

    private static FunctionCallContent WriteFile(string callId, string path, string content) =>
        new(callId, "WriteFile", new Dictionary<string, object?> { ["path"] = path, ["content"] = content });

    private sealed class Harness : IAsyncDisposable
    {
        private readonly AgentFactory _factory;

        private Harness(
            AgentFactory factory,
            SessionService service,
            MemoryExtractionScheduler scheduler,
            TraceStore traceStore,
            SessionThread thread)
        {
            _factory = factory;
            Service = service;
            Scheduler = scheduler;
            TraceStore = traceStore;
            Thread = thread;
            service.ThreadCreatedForBroadcast += created => Created.Enqueue(created.Id);
            service.ThreadDeletedForBroadcast += Deleted.Enqueue;
        }

        public SessionService Service { get; }

        public MemoryExtractionScheduler Scheduler { get; }

        public TraceStore TraceStore { get; }

        public SessionThread Thread { get; }

        public ConcurrentQueue<string> Created { get; } = new();

        public ConcurrentQueue<string> Deleted { get; } = new();

        public static async Task<Harness> StartAsync(
            MemoryExtractionEndToEndTests test,
            ScriptedChatClient chat,
            IRemoteToolHostClient? remote = null)
        {
            var config = AppConfigTestFactory.CreateOpenAI();
            config.Memory.AutoExtract = true;
            var registry = new ContributionRegistry();
            SystemPromptSectionCatalog.RegisterBuiltIns(registry);
            ChatMiddlewareCatalog.RegisterBuiltIns(registry);
            CompactableToolPolicyCatalog.RegisterBuiltIns(registry);
            CompactionSummarizerCatalog.RegisterBuiltIns(registry);
            AgentContextSourceCatalog.RegisterBuiltIns(registry);
            var router = new ToolInvocationRecorderRouter();
            var traceStore = new TraceStore(new WorkspaceStateDatabase(test._craft), maxEventsPerSession: 5000, synchronousPersist: true);
            var traceCollector = new TraceCollector(traceStore);
            var approval = new SessionScopedApprovalService(new AutoApproveApprovalService());
            var approvalStage = new CommonToolApprovalEvaluator();
            approvalStage.Bind(approval);
            var skills = new SkillsLoader(test._craft);
            var chatClientRegistry = TestModelProviderRegistry.Create();
            var factory = new AgentFactory(
                dotcraftPath: test._craft,
                workspacePath: test._workspace,
                config: config,
                memoryStore: test._memoryStore,
                skillsLoader: skills,
                approvalService: approval,
                blacklist: null,
                runtimeContext: new AgentRuntimeContext
                {
                    Config = config,
                    ChatClient = chat,
                    ChatClientRegistry = chatClientRegistry,
                    WorkspacePath = test._workspace,
                    BotPath = test._craft,
                    MemoryStore = test._memoryStore,
                    SkillsLoader = skills,
                    ContextPageManager = new ContextPageManager(),
                    ApprovalService = approval,
                    Contributions = registry,
                    TraceCollector = traceCollector
                },
                traceCollector: traceCollector,
                chatClientRegistry: chatClientRegistry,
                chatClient: chat,
                toolDispatcher: new ToolDispatcher(approvalEvaluator: approvalStage, recorder: router, contributions: registry),
                toolSources: [new CoreToolSource(config, chatClientRegistry, skills, approval, new StubBackgroundTerminalService())],
                remoteToolHostClient: remote);
            var service = new SessionService(
                factory,
                defaultAgent: null,
                new SessionPersistenceService(new ThreadStore(test._craft)),
                new SessionGate(),
                traceCollector: traceCollector);
            router.Bind(service);
            var scheduler = new MemoryExtractionScheduler(
                service.GetThreadAsync,
                (thread, ct) => service.ReadThreadTurnsAsync(thread.Id, ThreadHistorySortDirection.Descending, ct: ct),
                new MemoryExtractionRunner(service, registry, test._memoryStore, test._craft, traceCollector));
            registry.Add<ITurnLifecycleContributor>(scheduler);
            var thread = await service.CreateThreadAsync(
                new SessionIdentity { ChannelName = "test", UserId = "user", WorkspacePath = test._workspace });
            return new Harness(factory, service, scheduler, traceStore, thread);
        }

        public async Task SubmitAsync(string text)
        {
            await foreach (var _ in Service.SubmitInputAsync(Thread.Id, [new TextContent(text)]))
            {
            }
        }

        public async Task<string> SerializeTurnsAsync() =>
            JsonSerializer.Serialize((await Service.GetThreadAsync(Thread.Id)).Turns, SessionJsonOptions.Default);

        public JsonDocument Outcome(string forkId) =>
            JsonDocument.Parse(Assert.Single(
                TraceStore.GetEvents(forkId),
                evt => evt.Content == "Memory extraction outcome").MetadataJson!);

        public async ValueTask DisposeAsync()
        {
            Scheduler.Dispose();
            await _factory.DisposeAsync();
        }
    }

    private sealed class RoutedRemoteClient : IRemoteToolHostClient
    {
        private readonly ConcurrentDictionary<string, RemoteToolRoute> _routes = new(StringComparer.Ordinal);
        private int _remoteCalls;

        public event Action<RemoteToolRouteChange>? RouteChanged { add { } remove { } }

        public ConcurrentQueue<string> Forked { get; } = new();

        public int RemoteCalls => Volatile.Read(ref _remoteCalls);

        public void Connect(string threadId) =>
            _routes[threadId] = new RemoteToolRoute("host-1", "workspace-1", "lease-1", "instance-1", "session-1");

        public void UpdateRemoteToolSnapshot(string threadId, EffectiveToolSnapshot snapshot, string mode) { }

        public ValueTask PrepareTurnAsync(string threadId, EffectiveToolSnapshot snapshot, string mode,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void ReportTurn(string threadId, string turnId, TurnStatus status) { }

        public ValueTask<RemoteToolHostCatalog> ListAsync(string threadId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<RemoteToolConnectResult> ConnectAsync(string threadId, string hostId, string workspaceId,
            CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) =>
            throw new NotSupportedException();

        public ValueTask<RemoteToolDisconnectResult> DisconnectAsync(string threadId,
            CancellationToken cancellationToken = default, RemoteToolRouteInitiator initiator = RemoteToolRouteInitiator.Client) =>
            ValueTask.FromResult(_routes.TryRemove(threadId, out var route)
                ? new RemoteToolDisconnectResult(true, route)
                : new RemoteToolDisconnectResult(false));

        public bool TryGetRoute(string threadId, out RemoteToolRoute route) => _routes.TryGetValue(threadId, out route!);

        public bool TryGetConnectionSnapshot(string threadId, out RemoteToolConnectionSnapshot snapshot)
        {
            snapshot = null!;
            return false;
        }

        public bool TryForkRoute(string parentThreadId, string childThreadId)
        {
            if (!_routes.TryGetValue(parentThreadId, out var route))
                return false;
            _routes[childThreadId] = route;
            Forked.Enqueue(childThreadId);
            return true;
        }

        public ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId, byte[] bytes,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<byte[]> ReadImageAsync(RemoteToolRoute route, string threadId, string callId, string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<ToolExecutionResult> InvokeAsync(RemoteToolRoute route, ToolDefinition definition, string contractHash,
            ToolInvocationContext context, JsonObject arguments, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _remoteCalls);
            return ValueTask.FromResult(ToolExecutionResult.Succeeded("Written on the remote workspace."));
        }
    }

    private sealed record RecordedRequest(
        IReadOnlyList<string> Messages,
        string? Instructions,
        IReadOnlyList<string> Tools,
        string? CacheKey);

    private sealed class ScriptedChatClient(IReadOnlyList<FunctionCallContent> extractionCalls, bool repeatCalls = false)
        : IChatClient
    {
        private readonly ConcurrentQueue<RecordedRequest> _threadRequests = new();
        private readonly ConcurrentQueue<RecordedRequest> _extractionRequests = new();

        public IReadOnlyList<RecordedRequest> ThreadRequests => _threadRequests.ToArray();

        public IReadOnlyList<RecordedRequest> ExtractionRequests => _extractionRequests.ToArray();

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var messages = chatMessages.ToList();
            var request = new RecordedRequest(
                messages.Select(message => $"{message.Role}:{message.Text}").ToArray(),
                options?.Instructions,
                options?.Tools?.Select(tool => tool.Name).ToArray() ?? [],
                ProviderPromptCacheMetadata.ResolveKey(options));
            var extractionIndex = messages.FindLastIndex(message =>
                message.Role == ChatRole.User && message.Text.Contains(ExtractionMarker, StringComparison.Ordinal));
            if (extractionIndex < 0)
            {
                _threadRequests.Enqueue(request);
                yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
                yield break;
            }

            _extractionRequests.Enqueue(request);
            if (!repeatCalls && messages.Skip(extractionIndex).Any(message => message.Role == ChatRole.Tool))
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "Saved.");
                yield break;
            }

            var round = _extractionRequests.Count;
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                extractionCalls
                    .Select(call => (AIContent)new FunctionCallContent($"{call.CallId}-{round}", call.Name, call.Arguments))
                    .ToList());
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
