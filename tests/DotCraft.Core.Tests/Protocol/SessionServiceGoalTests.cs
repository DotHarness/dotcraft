using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
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
            toolSources: Array.Empty<IToolSource>());
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
        Assert.Equal(1, chatClient.StreamingCallCount);
        var loadedThread = await service.GetThreadAsync(thread.Id);
        Assert.DoesNotContain(loadedThread.QueuedInputs, IsGoalBudgetQueuedInput);
    }

    [Fact]
    public async Task SubmitInputAsync_AccountsBudgetLimitedGoalUsage()
    {
        var chatClient = new RecordingUsageChatClient([
            UsageUpdate(input: 7, output: 3),
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
            new ThreadGoalUpdate { Objective = "Wrap up within budget", TokenBudget = 1, HasTokenBudget = true });
        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Status = ThreadGoalStatus.BudgetLimited },
            GoalSetMode.UpdateOnly);

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("wrap up")]));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(ThreadGoalStatus.BudgetLimited, goal.Status);
        Assert.Equal(10, goal.TokensUsed.TotalTokens);
        Assert.Equal(1, chatClient.StreamingCallCount);
        var loadedThread = await service.GetThreadAsync(thread.Id);
        Assert.DoesNotContain(loadedThread.QueuedInputs, IsGoalBudgetQueuedInput);
    }

    [Fact]
    public async Task SubmitInputAsync_InjectsBudgetLimitSteeringAfterToolCompletion()
    {
        var chatClient = new ScriptedStreamingChatClient(
            [
                UsageUpdate(input: 9, output: 3),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-noop", "NoopTool", new Dictionary<string, object?>())
                ])
            ],
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("wrapped")])
            ]);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false });
        var service = CreateStreamingService(
            agentFactory,
            chatClient,
            [AIFunctionFactory.Create(() => "tool ok", name: "NoopTool")]);
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
        Assert.Equal(2, chatClient.CapturedRequests.Count);
        Assert.Contains(chatClient.CapturedRequests[1], message =>
            message.Role == ChatRole.System
            && MessageText(message).Contains("budget_limited", StringComparison.Ordinal)
            && MessageText(message).Contains("Stay in budget", StringComparison.Ordinal));
        var loadedThread = await service.GetThreadAsync(thread.Id);
        Assert.DoesNotContain(loadedThread.QueuedInputs, IsGoalBudgetQueuedInput);
    }

    [Fact]
    public async Task SubmitInputAsync_ParallelToolCompletionAccountsBudgetUsageOnce()
    {
        var chatClient = new ScriptedStreamingChatClient(
            [
                UsageUpdate(input: 9, output: 3),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-a", "NoopTool", new Dictionary<string, object?>()),
                    new FunctionCallContent("call-b", "NoopTool", new Dictionary<string, object?>())
                ])
            ],
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("wrapped")])
            ]);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false });
        var service = CreateStreamingService(
            agentFactory,
            chatClient,
            [AIFunctionFactory.Create(async () =>
            {
                await Task.Delay(25);
                return "tool ok";
            }, name: "NoopTool")],
            allowConcurrentInvocation: true);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Stay in budget once", TokenBudget = 10, HasTokenBudget = true });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")]));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(12, goal.TokensUsed.TotalTokens);
        Assert.Equal(ThreadGoalStatus.BudgetLimited, goal.Status);
        var budgetSteeringCount = chatClient.CapturedRequests
            .SelectMany(request => request)
            .Count(message => message.Role == ChatRole.System
                && MessageText(message).Contains("budget_limited", StringComparison.Ordinal));
        Assert.Equal(1, budgetSteeringCount);
    }

    [Fact]
    public async Task SubmitInputAsync_UpdateGoalToolDoesNotInjectBudgetLimitSteering()
    {
        var chatClient = new ScriptedStreamingChatClient(
            [
                UsageUpdate(input: 9, output: 3),
                new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent(
                        "call-update",
                        GoalToolNames.UpdateGoal,
                        new Dictionary<string, object?> { ["status"] = "complete" })
                ])
            ],
            [
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("reported")])
            ]);
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = false });
        var goalTools = agentFactory.CreateToolsForMode(AgentMode.Agent)
            .Where(tool => tool.Name is GoalToolNames.GetGoal or GoalToolNames.CreateGoal or GoalToolNames.UpdateGoal)
            .ToList();
        var service = CreateStreamingService(agentFactory, chatClient, goalTools, useFactoryAgent: true);
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Complete despite budget", TokenBudget = 10, HasTokenBudget = true });

        await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("finish")]));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(ThreadGoalStatus.Complete, goal.Status);
        Assert.Equal(12, goal.TokensUsed.TotalTokens);
        Assert.DoesNotContain(chatClient.CapturedRequests.SelectMany(request => request), message =>
            message.Role == ChatRole.System
            && MessageText(message).Contains("budget_limited", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryStartNextQueuedTurnAsync_RemovesLegacyGoalBudgetQueuedInput()
    {
        var chatClient = new RecordingUsageChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("should not run")])
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
        using (TurnTriggerScope.Set(new TurnTriggerInfo
        {
            Kind = "goal",
            Label = "Goal budget reached",
            RefId = "goal_legacy"
        }))
        {
            await service.EnqueueTurnInputAsync(
                thread.Id,
                [new TextContent("legacy budget guidance")],
                inputSnapshot: new SessionInputSnapshot
                {
                    DisplayText = "Goal budget reached",
                    NativeInputParts = [new SessionWireInputPart { Type = "text", Text = "Goal budget reached" }],
                    MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = "legacy budget guidance" }]
                });
        }

        await service.TryStartNextQueuedTurnAsync(thread.Id);

        var loadedThread = await service.GetThreadAsync(thread.Id);
        Assert.Empty(loadedThread.QueuedInputs);
        Assert.Empty(loadedThread.Turns);
        Assert.Equal(0, chatClient.StreamingCallCount);
    }

    [Fact]
    public async Task SetThreadGoalAsync_ObjectiveUpdate_PreservesUsageAndCreatedAt()
    {
        var thread = await _service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        var original = await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "First objective", TokenBudget = 100, HasTokenBudget = true });
        await _store.AccountThreadGoalUsageAsync(
            thread.Id,
            original.GoalId,
            new TokenUsageInfo { InputTokens = 8, TotalTokens = 8 },
            2,
            GoalAccountingMode.ActiveOnly);

        var updated = await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Updated objective" });

        Assert.Equal(original.GoalId, updated.GoalId);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.Equal("Updated objective", updated.Objective);
        Assert.Equal(8, updated.TokensUsed.TotalTokens);
        Assert.Equal(2, updated.TimeUsedSeconds);
        Assert.Equal(100, updated.TokenBudget);
    }

    [Fact]
    public async Task SetThreadGoalAsync_CreateOnly_FailsUntilExistingGoalIsComplete()
    {
        var thread = await _service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test",
            UserId = "user1",
            WorkspacePath = _tempDir
        });
        var first = await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "First objective" },
            GoalSetMode.CreateOnly);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.SetThreadGoalAsync(
                thread.Id,
                new ThreadGoalUpdate { Objective = "Second objective" },
                GoalSetMode.CreateOnly));

        await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Status = ThreadGoalStatus.Complete },
            GoalSetMode.UpdateOnly);

        var second = await _service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Second objective" },
            GoalSetMode.CreateOnly);

        Assert.NotEqual(first.GoalId, second.GoalId);
        Assert.Equal("Second objective", second.Objective);
        Assert.Equal(ThreadGoalStatus.Active, second.Status);
        Assert.Equal(0, second.TokensUsed.TotalTokens);
    }

    [Fact]
    public async Task GoalTools_AreSchemaStableAcrossAgentAndPlanModes()
    {
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        var config = AppConfigTestFactory.CreateOpenAI();
        await using var agentFactory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolSources: [new GoalToolSource(config)]);

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
    public async Task GoalToolSource_HidesGoalToolsFromSubAgentChildren()
    {
        var config = AppConfigTestFactory.CreateOpenAI();
        var source = new GoalToolSource(config);
        var context = new ToolPlanningContext(
            "thread_child",
            "turn_child",
            _tempDir,
            "agent",
            profile: null,
            providerCapabilities: ["subagent-child"],
            revision: 1);

        var registrations = await source.GetRegistrationsAsync(context);

        Assert.Empty(registrations);
    }

    [Fact]
    public async Task SetThreadGoalAsync_OnEmptyThread_DoesNotStartAutoContinuation()
    {
        var chatClient = new CompleteGoalChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient, config =>
            config.Goals = new AppConfig.GoalsConfig { AutoContinueEnabled = true });
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateDefaultAgent(),
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
        var service = new SessionService(
            agentFactory,
            agentFactory.CreateDefaultAgent(),
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

    [Fact]
    public async Task SubmitInputAsync_PersistsSentAsGoal_OnUserMessageItem()
    {
        var chatClient = new RecordingUsageChatClient([
            UsageUpdate(input: 1, output: 1),
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

        // A turn flagged sentAsGoal must stamp the durable marker on the persisted user item;
        // an ordinary turn must not (clients read this instead of matching objective text).
        await DrainAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("Ship the thing")],
            inputSnapshot: new SessionInputSnapshot { SentAsGoal = true }));
        await DrainAsync(service.SubmitInputAsync(
            thread.Id,
            [new TextContent("looks good")]));

        var loaded = await service.GetThreadAsync(thread.Id);
        Assert.Equal(2, loaded.Turns.Count);
        var goalPayload = Assert.IsType<UserMessagePayload>(loaded.Turns[0].Input!.Payload);
        Assert.True(goalPayload.SentAsGoal);
        var plainPayload = Assert.IsType<UserMessagePayload>(loaded.Turns[1].Input!.Payload);
        Assert.NotEqual(true, plainPayload.SentAsGoal);
    }

    [Fact]
    public async Task SetThreadGoalAsync_ExternalCompleteStopsInFlightAccounting()
    {
        var firstUsageAccounted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueStreaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new BlockingUsageChatClient(firstUsageAccounted, continueStreaming);
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
            new ThreadGoalUpdate { Objective = "Stop attribution on completion" });

        var drainTask = Task.Run(async () =>
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")])));
        await firstUsageAccounted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var completed = await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Status = ThreadGoalStatus.Complete },
            GoalSetMode.UpdateOnly);
        Assert.Equal(5, completed.TokensUsed.TotalTokens);

        continueStreaming.SetResult();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(ThreadGoalStatus.Complete, goal.Status);
        Assert.Equal(5, goal.TokensUsed.TotalTokens);
    }

    [Fact]
    public async Task SetThreadGoalAsync_ExternalMutationDoesNotAccountStoppedGoal()
    {
        var firstUsageAccounted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueStreaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chatClient = new BlockingUsageChatClient(firstUsageAccounted, continueStreaming);
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
            new ThreadGoalUpdate { Objective = "Do not account stopped progress" });

        var drainTask = Task.Run(async () =>
            await DrainAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")])));
        await firstUsageAccounted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var paused = await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Status = ThreadGoalStatus.Paused },
            GoalSetMode.UpdateOnly);
        Assert.Equal(5, paused.TokensUsed.TotalTokens);

        var updated = await service.SetThreadGoalAsync(
            thread.Id,
            new ThreadGoalUpdate { Objective = "Still paused" },
            GoalSetMode.UpdateOnly);
        Assert.Equal(ThreadGoalStatus.Paused, updated.Status);
        Assert.Equal(5, updated.TokensUsed.TotalTokens);

        continueStreaming.SetResult();
        await drainTask.WaitAsync(TimeSpan.FromSeconds(5));

        var goal = await service.GetThreadGoalAsync(thread.Id);
        Assert.NotNull(goal);
        Assert.Equal(ThreadGoalStatus.Paused, goal.Status);
        Assert.Equal(5, goal.TokensUsed.TotalTokens);
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient)
    {
        var defaultAgent = chatClient.AsAIAgent();
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private SessionService CreateStreamingService(
        AgentFactory agentFactory,
        IChatClient chatClient,
        IReadOnlyList<AITool> tools,
        bool allowConcurrentInvocation = false,
        bool useFactoryAgent = false)
    {
        var invokingClient = new StreamingFunctionInvokingChatClient(chatClient)
        {
            AllowConcurrentInvocation = allowConcurrentInvocation
        };
        var defaultAgent = useFactoryAgent
            ? agentFactory.CreateDefaultAgent()
            : invokingClient.AsAIAgent(new ChatOptions { Tools = tools.ToList() });
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
            chatClient: chatClient,
            toolSources: [new GoalToolSource(config)]);
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

    private static string MessageText(ChatMessage message) =>
        string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));

    private static bool IsGoalBudgetQueuedInput(QueuedTurnInput input) =>
        string.Equals(input.TriggerKind, "goal", StringComparison.Ordinal)
        && (string.Equals(input.TriggerLabel, "Goal budget reached", StringComparison.Ordinal)
            || string.Equals(input.DisplayText, "Goal budget reached", StringComparison.Ordinal));

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
        public int StreamingCallCount { get; private set; }

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
            StreamingCallCount++;
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

    private sealed class ScriptedStreamingChatClient(params ChatResponseUpdate[][] responses) : IChatClient
    {
        public List<List<ChatMessage>> CapturedRequests { get; } = [];

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
            var requestIndex = CapturedRequests.Count;
            CapturedRequests.Add(chatMessages.ToList());
            var updates = requestIndex < responses.Length
                ? responses[requestIndex]
                : Array.Empty<ChatResponseUpdate>();
            foreach (var update in updates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingUsageChatClient(
        TaskCompletionSource firstUsageAccounted,
        TaskCompletionSource continueStreaming) : IChatClient
    {
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
            yield return UsageUpdate(input: 3, output: 2);
            firstUsageAccounted.SetResult();
            await continueStreaming.Task.WaitAsync(cancellationToken);
            yield return UsageUpdate(input: 6, output: 4);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
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
