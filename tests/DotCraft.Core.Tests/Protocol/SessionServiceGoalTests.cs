using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceGoalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly SessionService _service;

    public SessionServiceGoalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSGoal_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(_store);
        var config = AppConfigTestFactory.CreateOpenAI();
        config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false };
        var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        _service = new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task SetThreadGoalAsync_CreatesReadableGoal()
    {
        var thread = await _service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

        var goal = await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Ship goal M1", TokenBudget = 12000, HasTokenBudget = true });

        Assert.Equal(thread.Id, goal.ThreadId);
        Assert.StartsWith("goal_", goal.GoalId);
        Assert.Equal("Ship goal M1", goal.Objective);
        Assert.Equal(ThreadGoalStatus.Active, goal.Status);
        Assert.Equal(12000, goal.TokenBudget);
        Assert.Equal(0, goal.TokensUsed.TotalTokens);

        var loaded = await _service.GetThreadGoalAsync(thread.Id);
        Assert.Equal(goal, loaded);
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsGoalContext_AndAccountsUsage()
    {
        var chatClient = new RecordingUsageChatClient([
            UsageUpdate(input: 12, output: 8),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false });
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Ship <runtime> safely", TokenBudget = 100, HasTokenBudget = true });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("continue")]));

        var captured = Assert.Single(chatClient.CapturedMessages);
        var text = string.Concat(captured.Contents.OfType<TextContent>().Select(content => content.Text));
        Assert.Contains("## Thread Goal", text);
        Assert.Contains("Status: Active", text);
        Assert.Contains("&lt;runtime&gt;", text);

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(20, goal.TokensUsed.TotalTokens);
        Assert.Equal(ThreadGoalStatus.Active, goal.Status);
    }

    [Fact]
    public async Task SubmitInputAsync_MarksGoalBudgetLimited_WhenBillingUsageReachesBudget()
    {
        var chatClient = new RecordingUsageChatClient([
            UsageUpdate(input: 9, output: 3),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false });
        var service = CreateService(agentFactory, chatClient);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Stay in budget", TokenBudget = 10, HasTokenBudget = true });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")]));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(12, goal.TokensUsed.TotalTokens);
        Assert.Equal(ThreadGoalStatus.BudgetLimited, goal.Status);
    }

    [Fact]
    public async Task GoalTools_AreSchemaStableAcrossAgentAndPlanModes()
    {
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: [new GoalToolProvider()]);

        var agentTools = agentFactory.CreateToolsForMode(AgentMode.Agent).Select(tool => tool.Name).ToArray();
        var planTools = agentFactory.CreateToolsForMode(AgentMode.Plan).Select(tool => tool.Name).ToArray();

        Assert.Contains(GoalToolNames.GetGoal, agentTools);
        Assert.Contains(GoalToolNames.CreateGoal, agentTools);
        Assert.Contains(GoalToolNames.UpdateGoal, agentTools);
        Assert.Contains(GoalToolNames.GetGoal, planTools);
        Assert.Contains(GoalToolNames.CreateGoal, planTools);
        Assert.Contains(GoalToolNames.UpdateGoal, planTools);
        Assert.Equal(agentTools, planTools);
    }

    [Fact]
    public async Task SetThreadGoalAsync_OnEmptyThread_DoesNotStartAutoContinuation()
    {
        var chatClient = new CompleteGoalChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = true });
        var goalTools = new GoalToolProvider().CreateTools(new ToolProviderContext
        {
            Config = agentFactory.ToolProviderContext.Config,
            ChatClient = null!,
            WorkspacePath = _tempDir,
            BotPath = _tempDir,
            MemoryStore = new MemoryStore(_tempDir),
            SkillsLoader = new SkillsLoader(_tempDir),
            ApprovalService = new AutoApproveApprovalService()
        }).ToList();
        var invokingClient = new StreamingFunctionInvokingChatClient(chatClient)
        {
            AllowConcurrentInvocation = true
        };
        var service = new SessionService(
            agentFactory,
            invokingClient.AsAIAgent(new ChatClientAgentOptions
            {
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions { Tools = goalTools }
            }),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());

        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Wait for the first user turn" });

        var started = await WaitUntilAsync(
            () => chatClient.CallCount > 0,
            TimeSpan.FromMilliseconds(300));

        var loadedThread = await service.GetThreadAsync(thread.Id);
        Assert.False(started);
        Assert.Equal(0, chatClient.CallCount);
        Assert.Empty(loadedThread.Turns);
    }

    [Fact]
    public async Task AutoContinuation_DoesNotInheritExternalGoalNotificationSuppression()
    {
        var chatClient = new CompleteGoalChatClient(completeOnCall: 2);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = true });
        var goalTools = new GoalToolProvider().CreateTools(new ToolProviderContext
        {
            Config = agentFactory.ToolProviderContext.Config,
            ChatClient = null!,
            WorkspacePath = _tempDir,
            BotPath = _tempDir,
            MemoryStore = new MemoryStore(_tempDir),
            SkillsLoader = new SkillsLoader(_tempDir),
            ApprovalService = new AutoApproveApprovalService()
        }).ToList();
        var invokingClient = new StreamingFunctionInvokingChatClient(chatClient)
        {
            AllowConcurrentInvocation = true
        };
        var service = new SessionService(
            agentFactory,
            invokingClient.AsAIAgent(new ChatClientAgentOptions
            {
                UseProvidedChatClientAsIs = true,
                ChatOptions = new ChatOptions { Tools = goalTools }
            }),
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
        var completedGoal = new TaskCompletionSource<(ThreadGoal Goal, string? TurnId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.ThreadGoalUpdatedForBroadcast = (goal, turnId) =>
        {
            if (goal.Status == ThreadGoalStatus.Complete)
                completedGoal.TrySetResult((goal, turnId));
        };

        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("first user request")]));

        using (SessionService.SuppressGoalBroadcastNotifications())
        {
            await service.SetThreadGoalAsync(
                thread.Id,
                new ThreadGoalUpdate { Objective = "Finish automatically" });
        }

        (ThreadGoal Goal, string? TurnId) result;
        try
        {
            result = await completedGoal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            var goal = await service.GetThreadGoalAsync(thread.Id);
            var loadedThread = await service.GetThreadAsync(thread.Id);
            throw new TimeoutException(
                $"Timed out waiting for complete goal broadcast. Calls={chatClient.CallCount}; goalStatus={goal?.Status}; turns={loadedThread.Turns.Count}; lastTurn={loadedThread.Turns.LastOrDefault()?.Status}; lastError={loadedThread.Turns.LastOrDefault()?.Error}");
        }
        Assert.Equal(ThreadGoalStatus.Complete, result.Goal.Status);
        Assert.Equal(thread.Id, result.Goal.ThreadId);
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient)
    {
        var defaultAgent = chatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient, Action<AppConfig>? configureConfig = null)
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        configureConfig?.Invoke(config);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: [new GoalToolProvider()]);
    }

    private static ChatResponseUpdate UsageUpdate(long input, long output) =>
        new()
        {
            Role = ChatRole.Assistant,
            Contents =
            [
                new UsageContent(new UsageDetails
                {
                    InputTokenCount = input,
                    OutputTokenCount = output
                })
            ]
        };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(25);
        }

        return condition();
    }

    private sealed class RecordingUsageChatClient(ChatResponseUpdate[] updates) : IChatClient
    {
        public List<ChatMessage> CapturedMessages { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedMessages.Clear();
            CapturedMessages.AddRange(chatMessages);
            foreach (var update in updates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class CompleteGoalChatClient(int completeOnCall = 1) : IChatClient
    {
        private int _callCount;
        public int CallCount => _callCount;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("done")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _callCount += 1;
            if (_callCount == completeOnCall)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent(
                        "call-complete-goal",
                        GoalToolNames.UpdateGoal,
                        new Dictionary<string, object?> { ["status"] = "complete" })
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("done")]);
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
