using System.Collections.Concurrent;
using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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
    public async Task SubmitInputAsync_WaitsForThreadAgentPublicationLock_BeforeUsingCachedAgent()
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
        var defaultAgent = new SignalingChatClient("default").AsAIAgent(new ChatClientAgentOptions());
        var svc = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
        var thread = await svc.CreateThreadAsync(identity);

        var threadChatClient = new SignalingChatClient("thread-agent");
        GetThreadAgents(svc)[thread.Id] = threadChatClient.AsAIAgent(new ChatClientAgentOptions());

        var agentLock = new SemaphoreSlim(0, 1);
        GetThreadAgentLocks(svc)[thread.Id] = agentLock;

        var events = svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")]);
        var drainTask = DrainAsync(events);

        var early = await Task.WhenAny(
            threadChatClient.StreamingStarted.Task,
            Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(threadChatClient.StreamingStarted.Task, early);

        agentLock.Release();
        await threadChatClient.StreamingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await drainTask.WaitAsync(TimeSpan.FromSeconds(2));
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
        var config = AppConfigTestFactory.CreateOpenAI(model: "model-a");
        config.Reasoning = new AppConfig.ReasoningConfig
        {
            Enabled = true,
            Effort = ReasoningEffort.High,
            Output = ReasoningOutput.Full
        };
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

        Assert.Equal("model-a", existingThread.Configuration?.Model);
        Assert.True(existingThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ReasoningEffort.High, existingThread.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Full, existingThread.Configuration?.Reasoning?.Output);
        Assert.False(GetThreadAgents(svc).ContainsKey(existingThread.Id));

        monitor.Current.Model = "model-b";
        monitor.Current.Reasoning = new AppConfig.ReasoningConfig
        {
            Enabled = false,
            Effort = ReasoningEffort.Low,
            Output = ReasoningOutput.Full
        };
        svc.InvalidateThreadAgents();

        var existingAfterChange = await svc.EnsureThreadLoadedAsync(existingThread.Id);
        var persistedExisting = await store.LoadThreadAsync(existingThread.Id);

        Assert.Equal("model-a", existingAfterChange.Configuration?.Model);
        Assert.True(existingAfterChange.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ReasoningEffort.High, existingAfterChange.Configuration?.Reasoning?.Effort);
        Assert.Equal("model-a", persistedExisting?.Configuration?.Model);
        Assert.True(persistedExisting?.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ReasoningEffort.High, persistedExisting?.Configuration?.Reasoning?.Effort);

        var newThread = await svc.CreateThreadAsync(identity);
        Assert.Equal("model-b", newThread.Configuration?.Model);
        Assert.False(newThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ReasoningEffort.Low, newThread.Configuration?.Reasoning?.Effort);

        var explicitThread = await svc.CreateThreadAsync(
            identity,
            new ThreadConfiguration
            {
                Model = "model-c",
                Reasoning = new AppConfig.ReasoningConfig
                {
                    Enabled = true,
                    Effort = ReasoningEffort.Medium,
                    Output = ReasoningOutput.Summary
                }
            });
        Assert.Equal("model-c", explicitThread.Configuration?.Model);
        Assert.True(explicitThread.Configuration?.Reasoning?.Enabled);
        Assert.Equal(ReasoningEffort.Medium, explicitThread.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, explicitThread.Configuration?.Reasoning?.Output);
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        return CreateAgentFactory(config);
    }

    private AgentFactory CreateAgentFactory(AppConfig config)
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
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private static AIAgent GetCachedThreadAgent(SessionService svc, string threadId)
    {
        var dict = GetThreadAgents(svc);
        Assert.True(dict.TryGetValue(threadId, out var agent));
        return agent;
    }

    private static ConcurrentDictionary<string, AIAgent> GetThreadAgents(SessionService svc)
    {
        var field = typeof(SessionService).GetField("_threadAgents", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (ConcurrentDictionary<string, AIAgent>)field.GetValue(svc)!;
    }

    private static ConcurrentDictionary<string, SemaphoreSlim> GetThreadAgentLocks(SessionService svc)
    {
        var field = typeof(SessionService).GetField("_threadAgentLocks", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (ConcurrentDictionary<string, SemaphoreSlim>)field.GetValue(svc)!;
    }

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static IChatClient? GetInnermostChatClient(AIAgent agent)
    {
        var client = TryGetChatClientFromAgent(agent);
        return UnwrapToInnermost(client);
    }

    private static IChatClient? TryGetChatClientFromAgent(AIAgent agent)
    {
        for (var t = agent.GetType(); t != null; t = t.BaseType)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(IChatClient).IsAssignableFrom(p.PropertyType))
                    continue;
                if (p.GetIndexParameters().Length != 0)
                    continue;
                if (p.GetValue(agent) is IChatClient chat)
                    return chat;
            }
        }

        return null;
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
}
