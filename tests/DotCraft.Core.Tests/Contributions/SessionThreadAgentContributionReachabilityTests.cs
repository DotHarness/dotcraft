using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.CompilerServices;
using DotCraft.Agents;
using DotCraft.Commands.Core;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Contributions;
using DotCraft.Memory;
using DotCraft.Runtime;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;

namespace DotCraft.Tests.Contributions;

/// <summary>Covers the contribution points a real thread's agent is built with. The factory-level agent is not the one a
/// session runs on, so every assertion here goes through <see cref="SessionService"/>'s thread build path.</summary>
public sealed class SessionThreadAgentContributionReachabilityTests : IDisposable
{
    private const string PromptMarker = "CONTRIBUTION-PROMPT-MARKER";
    private const string ContextMarker = "CONTRIBUTION-CONTEXT-MARKER";
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(30);

    private readonly string _tempDir;
    private readonly string _overrideDir;
    private readonly ContributionRegistry _registry = new();
    private readonly List<AgentFactory> _factories = [];

    public SessionThreadAgentContributionReachabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ThreadContribution_" + Guid.NewGuid().ToString("N")[..8]);
        _overrideDir = Path.Combine(_tempDir, "override-workspace");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_overrideDir);
        SystemPromptSectionCatalog.RegisterBuiltIns(_registry);
        ChatMiddlewareCatalog.RegisterBuiltIns(_registry);
        AgentContextSourceCatalog.RegisterBuiltIns(_registry);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedPromptSection_ReachesThePromptTheThreadAgentSends(bool workspaceOverride)
    {
        _registry.Add<ISystemPromptSection>(
            new MarkerSection("marker", PromptMarker),
            new ContributionOptions(Order: 50));
        var chatClient = new CapturingChatClient();
        var service = CreateService(chatClient);

        await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Contains(PromptMarker, chatClient.LastInstructions ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutContributions_TheThreadAgentKeepsOnlyTheBuiltInProvider(bool workspaceOverride)
    {
        var service = CreateService(new CapturingChatClient());

        var run = await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Single(run.Agent.AIContextProviders);
        Assert.IsType<MemoryContextProvider>(run.Agent.AIContextProviders[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedChatMiddleware_IsInTheThreadAgentClientChain(bool workspaceOverride)
    {
        var middleware = new MarkerMiddleware();
        _registry.Add<IChatMiddleware>(middleware, new ContributionOptions(Order: 50));
        var service = CreateService(new CapturingChatClient());

        var run = await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Contains(nameof(MarkerChatClient), ChainTypes(run.Agent.ChatClient));
        Assert.True(middleware.Invocations > 0, "The contributed middleware never saw the thread's request.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedAgentContext_IsInTheThreadAgentProviderList(bool workspaceOverride)
    {
        _registry.Add<IAgentContextSource>(new MarkerContextContribution());
        var chatClient = new CapturingChatClient();
        var service = CreateService(chatClient);

        var run = await RunThreadTurnAsync(service, workspaceOverride);

        var provider = Assert.Single(run.Agent.AIContextProviders.OfType<MarkerContextProvider>());
        Assert.Contains(ContextMarker, chatClient.LastInstructions ?? string.Empty, StringComparison.Ordinal);
        // The provider is parameterized from the thread's own context, not the factory-level one.
        var expectedWorkspace = workspaceOverride ? _overrideDir : _tempDir;
        Assert.Equal(Path.GetFullPath(expectedWorkspace), Path.GetFullPath(provider.Request.WorkspacePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MemoryTargetReplacement_OwnsThePromptTheThreadAgentSends(bool workspaceOverride)
    {
        _registry.Add<IAgentContextSource>(
            new MarkerContextContribution(),
            new ContributionOptions(ReplaceTarget: AgentContextSourceNames.Memory));
        var chatClient = new CapturingChatClient();
        var service = CreateService(chatClient);

        var run = await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Empty(run.Agent.AIContextProviders.OfType<MemoryContextProvider>());
        Assert.Equal(ContextMarker, chatClient.LastInstructions);
        Assert.NotNull(Assert.Single(run.Agent.AIContextProviders.OfType<MarkerContextProvider>())
            .Request.PromptInputs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedToolRestriction_MasksTheToolInTheThreadsOwnSnapshot(bool workspaceOverride)
    {
        _registry.Add<IToolSource>(new MarkerToolSource("marker", ["kept", "masked"]));
        _registry.Add<IToolRestriction>(new MarkerRestriction("masked"));
        var service = CreateService(new CapturingChatClient());

        var run = await RunThreadTurnAsync(service, workspaceOverride);

        var snapshot = service.DebugGetRuntime(run.ThreadId)?.LatestToolSnapshot;
        Assert.NotNull(snapshot);
        Assert.Contains(new ToolName(null, "kept"), snapshot.Registrations.Keys);
        Assert.DoesNotContain(new ToolName(null, "masked"), snapshot.Registrations.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedSubAgentRuntime_IsListedInThePromptTheThreadAgentSends(bool workspaceOverride)
    {
        // The profiles section is only assembled when the thread actually holds the spawn tool.
        _registry.Add<IToolSource>(new MarkerToolSource("marker", ["SpawnAgent"]));
        _registry.Add<ISubAgentRuntimeSource>(new MarkerSubAgentRuntimeContribution());
        var chatClient = new CapturingChatClient();
        var service = CreateService(chatClient);

        await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Contains(
            $"`{MarkerSubAgentRuntimeContribution.ProfileName}`",
            chatClient.LastInstructions ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContributedCommand_IsDescribedInThePromptTheThreadAgentSends(bool workspaceOverride)
    {
        // No CustomCommandLoader reaches the agent factory, so this is the only path that puts a
        // custom command in a real thread's prompt.
        _registry.Add<ICodeCommand>(new MarkerCommand());
        var chatClient = new CapturingChatClient();
        var service = CreateService(chatClient);

        await RunThreadTurnAsync(service, workspaceOverride);

        Assert.Contains(
            $"`/{MarkerCommand.CommandName}`: {MarkerCommand.CommandDescription}",
            chatClient.LastInstructions ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContributedRuntimeSignalObserver_SeesTheRealTurnsSignals()
    {
        var observer = new RecordingSignalContributor();
        _registry.Add<IThreadRuntimeSignalContributor>(observer);
        var service = CreateService(new CapturingChatClient());
        using var dispatcher = AttachRuntimeSignals(service);

        var run = await RunThreadTurnAsync(service, workspaceOverride: false);

        Assert.True(dispatcher.WaitForPendingSignals(SignalTimeout));
        Assert.Contains(SessionThreadRuntimeSignal.TurnStarted, observer.Signals);
        Assert.Contains(SessionThreadRuntimeSignal.TurnCompleted, observer.Signals);
        Assert.All(observer.Threads, threadId => Assert.Equal(run.ThreadId, threadId));
    }

    [Fact]
    public async Task ARuntimeSignalObserverThatNeverReturns_DoesNotStallTheTurn()
    {
        using var gate = new ManualResetEventSlim(false);
        var stalled = new BlockingSignalContributor(gate);
        _registry.Add<IThreadRuntimeSignalContributor>(stalled);
        var service = CreateService(new CapturingChatClient());
        using var dispatcher = AttachRuntimeSignals(service);

        // A stalled observer turns a turn-path stall into a hang, so the turn is bounded by a timeout.
        await RunThreadTurnAsync(service, workspaceOverride: false).WaitAsync(SignalTimeout);

        Assert.True(stalled.Entered.Wait(SignalTimeout));
        Assert.Equal(1, stalled.Entries);
        gate.Set();
        Assert.True(dispatcher.WaitForPendingSignals(SignalTimeout));
    }

    [Fact]
    public async Task RegistryMutationAfterTheThreadExists_ReachesTheRebuiltThreadAgent()
    {
        var service = CreateService(new CapturingChatClient());
        using var bridge = new ContributionPropagationBridge(_registry, service);
        var run = await RunThreadTurnAsync(service, workspaceOverride: false);
        Assert.Empty(run.Agent.AIContextProviders.OfType<MarkerContextProvider>());

        _registry.Add<IAgentContextSource>(new MarkerContextContribution());

        // The mutation reaches the live thread through the bridge's invalidation, not through a fresh session.
        Assert.Null(service.DebugGetRuntime(run.ThreadId)?.Agent);
        await service.RefreshThreadAgentAsync(run.ThreadId);
        var rebuilt = service.DebugGetRuntime(run.ThreadId)?.Agent;
        Assert.NotNull(rebuilt);
        Assert.Single(rebuilt.AIContextProviders.OfType<MarkerContextProvider>());
    }

    /// <summary>Runs one real turn on a thread and returns the agent the turn ran on.</summary>
    private async Task<(string ThreadId, ChatClientAgent Agent)> RunThreadTurnAsync(
        SessionService service,
        bool workspaceOverride)
    {
        // Pins per-thread agents, exactly as an installed plugin or a contribution change does.
        service.InvalidateThreadAgents();
        var thread = await service.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = "test",
                UserId = "user1",
                WorkspacePath = _tempDir
            },
            workspaceOverride ? new ThreadConfiguration { WorkspaceOverride = _overrideDir } : null);

        await foreach (var _ in service.SubmitInputAsync(thread.Id, [new TextContent("hello")]))
        {
        }

        var agent = service.DebugGetRuntime(thread.Id)?.Agent;
        Assert.NotNull(agent);
        return (thread.Id, agent);
    }

    /// <summary>Wires the runtime signal funnel exactly as <c>WorkspaceRuntime</c> does.</summary>
    private ThreadRuntimeSignalDispatcher AttachRuntimeSignals(SessionService service)
    {
        var dispatcher = new ThreadRuntimeSignalDispatcher(_registry);
        service.ThreadRuntimeSignalForBroadcast = (threadId, signal, _) => dispatcher.Publish(threadId, signal);
        return dispatcher;
    }

    private SessionService CreateService(IChatClient chatClient)
    {
        var agentFactory = CreateAgentFactory(chatClient);
        return new SessionService(
            agentFactory,
            chatClient.AsAIAgent(),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var chatClientRegistry = TestModelProviderRegistry.Create();
        var memoryStore = new MemoryStore(_tempDir);
        var skillsLoader = new SkillsLoader(_tempDir);
        var factory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memoryStore,
            skillsLoader: skillsLoader,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            runtimeContext: new AgentRuntimeContext
            {
                Config = config,
                ChatClient = chatClient,
                ChatClientRegistry = chatClientRegistry,
                EffectiveProviderId = config.ProviderId,
                EffectiveProviderProtocol = ModelProviderProtocols.OpenAIChatCompletions,
                EffectiveMainModel = "gpt-4o-mini",
                WorkspacePath = _tempDir,
                BotPath = _tempDir,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ContextPageManager = new ContextPageManager(),
                ApprovalService = new AutoApproveApprovalService(),
                Contributions = _registry
            },
            chatClientRegistry: chatClientRegistry,
            chatClient: chatClient,
            toolSources: Array.Empty<IToolSource>());
        _factories.Add(factory);
        return factory;
    }

    // Outermost client inwards.
    private static IReadOnlyList<string> ChainTypes(IChatClient client)
    {
        var names = new List<string>();
        var current = client;
        while (current is not null)
        {
            names.Add(current.GetType().Name);
            current = current is DelegatingChatClient delegating
                ? typeof(DelegatingChatClient)
                    .GetProperty("InnerClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(delegating) as IChatClient
                : null;
        }

        return names;
    }

    public void Dispose()
    {
        foreach (var factory in _factories)
            factory.DisposeAsync().AsTask().GetAwaiter().GetResult();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    private sealed class MarkerSection(string name, string content) : ISystemPromptSection
    {
        public string Name => name;

        public string GetContent(SystemPromptSectionContext context) => content;
    }

    private sealed class MarkerToolSource(string sourceId, IReadOnlyList<string> toolNames) : IToolSource
    {
        public string SourceId => sourceId;

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>(toolNames.Select(Create).ToArray());

        private static ToolRegistration Create(string name)
        {
            var definitionId = new ToolDefinitionId(ToolSourceKind.CoreNative, "marker", new SourceToolId(name));
            return new ToolRegistration(
                new ToolDefinition(
                    definitionId,
                    new ToolName(null, name),
                    $"Run {name}",
                    JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone()),
                new ToolRuntimeBinding(
                    new RuntimeBindingId($"binding-{name}"),
                    definitionId,
                    new MarkerToolRuntime(),
                    ToolBindingLeases.AlwaysAvailable,
                    $"authority:{name}",
                    revision: 1),
                ToolProjectionShape.StandardPair);
        }
    }

    private sealed class MarkerToolRuntime : IToolRuntime
    {
        public ValueTask<ToolExecutionResult> InvokeAsync(
            ToolInvocationContext context,
            JsonObject arguments,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ToolExecutionResult.Succeeded("ok"));
    }

    private sealed class MarkerRestriction(string toolName) : IToolRestriction
    {
        public string Name => "contribution-marker";

        public ToolRestrictionEdit? Restrict(ToolRestrictionContext context) =>
            string.Equals(context.Definition.Name.Name, toolName, StringComparison.Ordinal)
                ? new ToolRestrictionEdit { Mask = true }
                : null;
    }

    private sealed class MarkerMiddleware : IChatMiddleware
    {
        private int _invocations;

        public string Name => "contribution-marker";

        public int Invocations => Volatile.Read(ref _invocations);

        public IChatClient Wrap(IChatClient inner, ChatPipelineContext context) =>
            new MarkerChatClient(inner, () => Interlocked.Increment(ref _invocations));
    }

    private sealed class MarkerChatClient(IChatClient inner, Action onCall) : DelegatingChatClient(inner)
    {
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            onCall();
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            onCall();
            return base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
    }

    private sealed class MarkerCommand : ICodeCommand
    {
        internal const string CommandName = "contribution-triage";
        internal const string CommandDescription = "CONTRIBUTION-COMMAND-MARKER";

        public string Name => CommandName;

        public string Description => CommandDescription;

        public string? Expand(CommandInvocation invocation) => invocation.Arguments;
    }

    private sealed class MarkerSubAgentRuntimeContribution : ISubAgentRuntimeSource
    {
        public const string RuntimeTypeName = "contribution-marker-runtime";
        public const string ProfileName = "contribution-marker-profile";

        public ISubAgentRuntime Runtime { get; } = new MarkerSubAgentRuntime();

        public IReadOnlyList<SubAgentProfile> Profiles { get; } =
        [
            new() { Name = ProfileName, Runtime = RuntimeTypeName, WorkingDirectoryMode = "workspace" }
        ];
    }

    private sealed class MarkerSubAgentRuntime : ISubAgentRuntime
    {
        public string RuntimeType => MarkerSubAgentRuntimeContribution.RuntimeTypeName;

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));

        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DotCraft.Agents.SubAgentRunResult { Text = request.Task });

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class MarkerContextContribution : IAgentContextSource
    {
        public AIContextProvider CreateProvider(AgentContextRequest request) => new MarkerContextProvider(request);
    }

    private sealed class MarkerContextProvider(AgentContextRequest request) : AIContextProvider
    {
        public AgentContextRequest Request { get; } = request;

        protected override ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AIContext { Instructions = ContextMarker });
    }

    private sealed class RecordingSignalContributor : IThreadRuntimeSignalContributor
    {
        private readonly List<ThreadRuntimeSignalContext> _seen = [];

        public IReadOnlyList<SessionThreadRuntimeSignal> Signals
        {
            get
            {
                lock (_seen)
                    return _seen.Select(entry => entry.Signal).ToArray();
            }
        }

        public IReadOnlyList<string> Threads
        {
            get
            {
                lock (_seen)
                    return _seen.Select(entry => entry.ThreadId).ToArray();
            }
        }

        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default)
        {
            lock (_seen)
                _seen.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSignalContributor(ManualResetEventSlim gate) : IThreadRuntimeSignalContributor
    {
        private int _entries;

        public ManualResetEventSlim Entered { get; } = new(false);

        public int Entries => Volatile.Read(ref _entries);

        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _entries);
            Entered.Set();
            gate.Wait(TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public string? LastInstructions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastInstructions = options?.Instructions;
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
