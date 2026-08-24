using System.Reflection;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Extensions.AI;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using ModelPreference = DotCraft.Configuration.ModelPreference;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using ModelPreferenceContextWindow = DotCraft.Configuration.ModelPreferenceContextWindow;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Regression: <see cref="SessionService.SetThreadModeAsync"/> must rebuild the per-thread agent via
/// <see cref="SessionService"/> (same path as config updates) so <see cref="ThreadConfiguration.Model"/> is not lost.
/// </summary>
public sealed class SessionServiceSetThreadModeTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceSetThreadModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SetThreadMode_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task BindingMcpServers_RemainWhenThreadUserConfigurationIsDisabled()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        await using var agentFactory = CreateAgentFactory();
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateAgentForMode(AgentMode.Agent),
            persistence,
            new SessionGate());
        var thread = await service.CreateThreadAsync(
            new SessionIdentity { ChannelName = "test", UserId = "u", WorkspacePath = _tempDir },
            new ThreadConfiguration { Mode = "agent", McpServers = [] });

        await service.SetBindingMcpServersAsync(
            thread.Id,
            "board-binding",
            [new McpServerConfig { Name = "board", Enabled = false }]);
        var runtime = await service.GetEffectiveMcpRuntimeAsync(thread.Id);
        var configs = await runtime!.ListConfigsAsync();

        var binding = Assert.Single(configs);
        Assert.Equal("binding:board-binding:board", binding.Name);
        Assert.True(binding.Origin.IsBinding);
        Assert.Equal("board-binding", binding.Origin.BindingId);

        await service.SetBindingMcpServersAsync(thread.Id, "board-binding", []);

        var clearedRuntime = await service.GetEffectiveMcpRuntimeAsync(thread.Id);
        Assert.Empty(await clearedRuntime!.ListConfigsAsync());

        await service.UpdateThreadConfigurationAsync(
            thread.Id,
            new ThreadConfiguration { Mode = "agent", McpServers = null });

        Assert.Null(await service.GetEffectiveMcpRuntimeAsync(thread.Id));
    }

    [Fact]
    public async Task SetThreadModeAsync_KeepsPerThreadChatClient_WhenModelOverrideIsSet()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        const string modelOverride = "gpt-per-thread-model-override";

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());

        var config = new ThreadConfiguration
        {
            Mode = "agent",
            Model = modelOverride
        };
        var thread = await svc.CreateThreadAsync(identity, config);

        await svc.SetThreadModeAsync(thread.Id, "plan");

        var innerAfterModeSwitch = GetInnermostChatClient(GetCachedThreadAgent(svc, thread.Id));
        Assert.NotNull(innerAfterModeSwitch);

        var loaded = await store.LoadThreadAsync(thread.Id);
        Assert.Equal("plan", loaded?.Configuration?.Mode);
        Assert.Equal(modelOverride, loaded?.Configuration?.Model);

        // Old bug: CreateAgentForMode ignored thread.Model — inner client matched the global default agent.
        var defaultPlanAgent = agentFactory.CreateAgentForMode(AgentMode.Plan);
        var innerDefaultPlan = GetInnermostChatClient(defaultPlanAgent);
        Assert.NotSame(innerDefaultPlan, innerAfterModeSwitch);
    }

    [Fact]
    public async Task SubmitInputAsync_OrdersExecutionResourceCaptureBehindInFlightPublication()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = new SignalingChatClient("default").AsAIAgent();
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
        var thread = await svc.CreateThreadAsync(identity);

        var threadChatClient = new SignalingChatClient("thread-agent");
        SetCachedThreadAgent(svc, thread.Id, threadChatClient.AsAIAgent());

        var agentLock = new SemaphoreSlim(0, 1);
        var runtime = svc.DebugGetRuntime(thread.Id);
        Assert.NotNull(runtime);
        runtime!.AgentLock = agentLock;

        var events = svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")]);
        var drainTask = DrainAsync(events);

        var early = await Task.WhenAny(
            threadChatClient.StreamingStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(threadChatClient.StreamingStarted.Task, early);

        var publishedClient = new SignalingChatClient("published-agent");
        SetCachedThreadAgent(svc, thread.Id, publishedClient.AsAIAgent());
        agentLock.Release();
        await publishedClient.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(threadChatClient.StreamingStarted.Task.IsCompleted);
        await drainTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task SetThreadModeAsync_DuringActiveTurn_AppliesToNextTurnOnly()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        await using var agentFactory = CreateAgentFactory();
        var activeClient = new BlockingChatClient();
        var svc = new SessionService(
            agentFactory,
            activeClient.AsAIAgent(),
            persistence,
            new SessionGate());
        var thread = await svc.CreateThreadAsync(identity, new ThreadConfiguration { Mode = "agent" });

        var events = svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]);
        var drainTask = DrainAsync(events);
        await activeClient.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await svc.SetThreadModeAsync(thread.Id, "plan");

        var runtime = svc.DebugGetRuntime(thread.Id);
        Assert.NotNull(runtime);
        var activeTurn = Assert.Single(runtime!.Turns.Values);
        Assert.Equal("agent", activeTurn.Context?.Configuration.Mode);
        Assert.Equal("plan", runtime.Thread.Configuration?.Mode);

        activeClient.Release();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateThreadConfiguration_DuringActiveTurn_DefersRetiredCleanupWithoutTerminalRelease()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };
        var resourceSource = new TrackingThreadResourceSource();
        var config = AppConfigTestFactory.CreateOpenAI();

        await using var agentFactory = CreateAgentFactory(config, [resourceSource]);
        var activeClient = new BlockingChatClient();
        var svc = new SessionService(
            agentFactory,
            activeClient.AsAIAgent(),
            persistence,
            new SessionGate());
        var thread = await svc.CreateThreadAsync(identity, new ThreadConfiguration { Mode = "agent" });
        SetCachedThreadAgent(svc, thread.Id, activeClient.AsAIAgent());

        var events = svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]);
        var drainTask = DrainAsync(events);
        await activeClient.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await svc.UpdateThreadConfigurationAsync(
            thread.Id,
            new ThreadConfiguration { Mode = "plan" });

        var runtime = svc.DebugGetRuntime(thread.Id);
        Assert.NotNull(runtime);
        var activeTurn = Assert.Single(runtime!.Turns.Values);
        Assert.Equal("agent", activeTurn.Context?.Configuration.Mode);
        Assert.Equal("plan", runtime.Thread.Configuration?.Mode);
        Assert.Equal(0, resourceSource.TerminalReleaseCalls);
        Assert.Equal(0, resourceSource.RetiredReleaseCalls);

        activeClient.Release();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, resourceSource.RetiredReleaseCalls);
        Assert.Equal(0, resourceSource.TerminalReleaseCalls);

        await svc.ArchiveThreadAsync(thread.Id);
        Assert.Equal(1, resourceSource.TerminalReleaseCalls);
    }

    [Fact]
    public async Task CreateThreadAsync_CapturesWorkspaceModelAndReasoningForNewThreads()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };
        var config = AppConfigTestFactory.CreateOpenAI(model: "gpt-5.5");
        config.ProviderPreferences[config.ProviderId].Reasoning = new AppConfig.ReasoningConfig
        {
            Enabled = true,
            Effort = ModelReasoningEffort.High,
            Output = ReasoningOutput.Full
        };
        config.ProviderPreferences[config.ProviderId].ContextWindow.Mode = ContextWindowMode.Max;
        var monitor = new AppConfigMonitor(config);

        await using var agentFactory = CreateAgentFactory(config);
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(
            agentFactory,
            defaultAgent,
            persistence,
            new SessionGate(),
            appConfigMonitor: monitor);

        var existingThread = await svc.CreateThreadAsync(identity);

        Assert.Equal("gpt-5.5", existingThread.Configuration?.Model);
        Assert.True(existingThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.High, existingThread.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Full, existingThread.Configuration?.Reasoning?.Output);
        Assert.Equal(ContextWindowMode.Max, existingThread.Configuration?.ContextWindow?.Mode);
        Assert.NotNull(svc.DebugGetRuntime(existingThread.Id)?.Agent);

        monitor.Current.ProviderPreferences[monitor.Current.ProviderId] = new ModelPreference
        {
            Model = "model-b",
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = false,
            Effort = ModelReasoningEffort.Low,
                Output = ReasoningOutput.Full
            },
            ContextWindow = new ModelPreferenceContextWindow { Mode = ContextWindowMode.Default }
        };
        svc.InvalidateThreadAgents();

        var existingAfterChange = await svc.EnsureThreadLoadedAsync(existingThread.Id);
        var persistedExisting = await store.LoadThreadAsync(existingThread.Id);

        Assert.Equal("gpt-5.5", existingAfterChange.Configuration?.Model);
        Assert.True(existingAfterChange.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.High, existingAfterChange.Configuration?.Reasoning?.Effort);
        Assert.Equal(ContextWindowMode.Max, existingAfterChange.Configuration?.ContextWindow?.Mode);
        Assert.Equal("gpt-5.5", persistedExisting?.Configuration?.Model);
        Assert.True(persistedExisting?.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.High, persistedExisting?.Configuration?.Reasoning?.Effort);
        Assert.Equal(ContextWindowMode.Max, persistedExisting?.Configuration?.ContextWindow?.Mode);

        var newThread = await svc.CreateThreadAsync(identity);
        Assert.Equal("model-b", newThread.Configuration?.Model);
        Assert.False(newThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.Low, newThread.Configuration?.Reasoning?.Effort);
        Assert.Equal(ContextWindowMode.Default, newThread.Configuration?.ContextWindow?.Mode);

        var explicitThread = await svc.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                Model = "model-c",
                ContextWindow = new ThreadContextWindowConfig { Mode = ContextWindowMode.Default },
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = true,
                Effort = ModelReasoningEffort.Medium,
                    Output = ReasoningOutput.Summary
                }
            });
        Assert.Equal("model-c", explicitThread.Configuration?.Model);
        Assert.True(explicitThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ModelReasoningEffort.Medium, explicitThread.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, explicitThread.Configuration?.Reasoning?.Output);
        Assert.Equal(ContextWindowMode.Default, explicitThread.Configuration?.ContextWindow?.Mode);
    }

    [Fact]
    public async Task CreateThreadAsync_RepairsWorkspaceMaxContextWindowToDefaultForUnsupportedModels()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };
        var config = AppConfigTestFactory.CreateOpenAI(model: "model-a");
        config.Compaction.ContextWindowMode = ContextWindowMode.Max;
        var monitor = new AppConfigMonitor(config);

        await using var agentFactory = CreateAgentFactory(config);
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(
            agentFactory,
            defaultAgent,
            persistence,
            new SessionGate(),
            appConfigMonitor: monitor);

        var defaultThread = await svc.CreateThreadAsync(identity);
        var explicitModelThread = await svc.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                Model = "unknown-model"
            });
        var persistedDefaultThread = await store.LoadThreadAsync(defaultThread.Id);
        var persistedExplicitModelThread = await store.LoadThreadAsync(explicitModelThread.Id);

        Assert.Equal("model-a", defaultThread.Configuration?.Model);
        Assert.Equal(ContextWindowMode.Default, defaultThread.Configuration?.ContextWindow?.Mode);
        Assert.Equal(ContextWindowMode.Default, persistedDefaultThread?.Configuration?.ContextWindow?.Mode);
        Assert.Equal("unknown-model", explicitModelThread.Configuration?.Model);
        Assert.Equal(ContextWindowMode.Default, explicitModelThread.Configuration?.ContextWindow?.Mode);
        Assert.Equal(ContextWindowMode.Default, persistedExplicitModelThread?.Configuration?.ContextWindow?.Mode);
    }

    [Fact]
    public async Task ForkThreadAsync_PreservesContextWindowUnlessRequestOverridesIt()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());

        var source = await svc.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                Model = "gpt-5.5",
                ContextWindow = new ThreadContextWindowConfig { Mode = ContextWindowMode.Max }
            });

        var inheritedFork = await svc.ForkThreadAsync(source.Id, null);
        Assert.Equal(ContextWindowMode.Max, inheritedFork.Configuration?.ContextWindow?.Mode);

        var partialOverrideFork = await svc.ForkThreadAsync(
            source.Id,
            new ThreadForkOptions
            {
                Config = new ThreadConfiguration
                {
                    Model = "gpt-5.5"
                }
            });
        Assert.Equal(ContextWindowMode.Max, partialOverrideFork.Configuration?.ContextWindow?.Mode);

        var explicitDefaultFork = await svc.ForkThreadAsync(
            source.Id,
            new ThreadForkOptions
            {
                Config = new ThreadConfiguration
                {
                    Model = "gpt-5.5",
                    ContextWindow = new ThreadContextWindowConfig { Mode = ContextWindowMode.Default }
                }
            });
        Assert.Equal(ContextWindowMode.Default, explicitDefaultFork.Configuration?.ContextWindow?.Mode);
    }

    [Fact]
    public async Task CreateThreadAsync_PreservesAgentBuilderTargetConfiguration()
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var identity = new SessionIdentity
        {
            ChannelName = "test",
            UserId = "u",
            WorkspacePath = _tempDir
        };

        await using var agentFactory = CreateAgentFactory();
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());

        var thread = await svc.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                AgentBuilderTargetId = "draft-agent",
                AgentBuilderTargetSource = "workspace"
            });

        Assert.Equal("draft-agent", thread.Configuration?.AgentBuilderTargetId);
        Assert.Equal("workspace", thread.Configuration?.AgentBuilderTargetSource);
        Assert.True(ThreadVisibility.IsInternal(thread));

        var loaded = await store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal("draft-agent", loaded?.Configuration?.AgentBuilderTargetId);
        Assert.Equal("workspace", loaded?.Configuration?.AgentBuilderTargetSource);
        Assert.True(ThreadVisibility.IsInternal(loaded!));
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return CreateAgentFactory(config);
    }

    private AgentFactory CreateAgentFactory(
        AppConfig config,
        IEnumerable<IToolSource>? toolSources = null)
    {
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create(),
            toolSources: toolSources ?? Array.Empty<IToolSource>());
    }

    private static ChatClientAgent GetCachedThreadAgent(SessionService svc, string threadId)
    {
        var runtime = svc.DebugGetRuntime(threadId);
        Assert.NotNull(runtime);
        Assert.NotNull(runtime!.Agent);
        return runtime.Agent;
    }

    private static void SetCachedThreadAgent(SessionService svc, string threadId, ChatClientAgent agent)
    {
        var runtime = svc.DebugGetRuntime(threadId);
        Assert.NotNull(runtime);
        runtime!.Agent = agent;
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static IChatClient? GetInnermostChatClient(ChatClientAgent agent)
    {
        return UnwrapToInnermost(agent.ChatClient);
    }

    private static IChatClient? UnwrapToInnermost(IChatClient? client)
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (client != null && seen.Add(client))
        {
            IChatClient? next = null;
            var t = client.GetType();
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.PropertyType != typeof(IChatClient) && !typeof(IChatClient).IsAssignableFrom(p.PropertyType))
                    continue;
                if (p.GetIndexParameters().Length != 0)
                    continue;
                if (p.GetValue(client) is IChatClient inner)
                {
                    next = inner;
                    break;
                }
            }

            if (next == null)
                return client;
            client = next;
        }

        return client;
    }

    private sealed class SignalingChatClient(string responseText) : IChatClient
    {
        public TaskCompletionSource<object?> StreamingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            StreamingStarted.TrySetResult(null);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingStarted.TrySetResult(null);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<object?> StreamingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            StreamingStarted.TrySetResult(null);
            await _release.Task.WaitAsync(cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
        }

        public void Release() => _release.TrySetResult();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class TrackingThreadResourceSource
        : IToolSource, IThreadScopedToolSource, IThreadRetiredToolResourceSource
    {
        public string SourceId => "tracking-thread-resource";

        public int TerminalReleaseCalls { get; private set; }

        public int RetiredReleaseCalls { get; private set; }

        public ValueTask<IReadOnlyList<ToolRegistration>> GetRegistrationsAsync(
            ToolPlanningContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ToolRegistration>>([]);

        public ValueTask ReleaseThreadAsync(
            string threadId,
            CancellationToken cancellationToken = default)
        {
            TerminalReleaseCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseRetiredThreadResourcesAsync(
            string threadId,
            CancellationToken cancellationToken = default)
        {
            RetiredReleaseCalls++;
            return ValueTask.CompletedTask;
        }
    }
}
