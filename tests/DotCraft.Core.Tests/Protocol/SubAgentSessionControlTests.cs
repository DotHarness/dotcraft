using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using Microsoft.Extensions.AI;
using DotCraft.Sessions;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using SubAgentThreadSource = DotCraft.Sessions.SubAgentThreadSource;
using ThreadConfiguration = DotCraft.Sessions.ThreadConfiguration;
using ThreadSource = DotCraft.Sessions.ThreadSource;
using TestableSessionService = DotCraft.Tests.Sessions.Protocol.AppServer.CoreTestableSessionService;
using ThreadSpawnEdge = DotCraft.Sessions.ThreadSpawnEdge;
using ToolCallPayload = DotCraft.Sessions.ToolCallPayload;
using ToolResultPayload = DotCraft.Sessions.ToolResultPayload;
using UserMessagePayload = DotCraft.Sessions.UserMessagePayload;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SubAgentSessionControlTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly TestableSessionService _sessionService;

    public SubAgentSessionControlTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"subagent_session_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _sessionService = new TestableSessionService(_store);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task SpawnAgent_WithChildCreated_RefreshesFreshChildToolsBeforeFirstTurn()
    {
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("ok", false)));
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "none",
                ChildCreated = (_, _) => Task.CompletedTask
            },
            waitForCompletion: false,
            CancellationToken.None);

        Assert.Contains(result.ChildThreadId, _sessionService.RefreshedThreadAgents);
    }

    [Fact]
    public async Task SpawnAgent_RunsSubagentStartAndStopLifecycleHooks()
    {
        var events = new ConcurrentQueue<(HookEvent Event, string? AgentPath, string? Status)>();
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("ok", false)));
        var context = await CreateContextAsync(lifecycleHook: (request, _) =>
        {
            events.Enqueue((
                request.Event,
                request.ChildThread.Source.SubAgent?.AgentPath,
                request.Status));
            return Task.CompletedTask;
        });

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect"
            },
            waitForCompletion: false,
            CancellationToken.None);
        await _sessionService.WaitForTaskEndingAsync("/root/inspect", 1);
        await WaitUntilAsync(() => events.Any(evt => evt.Event == HookEvent.SubagentStop));

        var observed = events.ToArray();
        Assert.Equal([HookEvent.SubagentStart, HookEvent.SubagentStop], observed.Select(evt => evt.Event).ToArray());
        Assert.All(observed, evt => Assert.Equal("/root/inspect", evt.AgentPath));
        Assert.Equal("running", observed[0].Status);
        Assert.Equal("completed", observed[1].Status);
    }

    [Fact]
    public async Task SpawnAgent_ChildOutlivesCallerTokenButNotParentTurn()
    {
        ConfigureNativeSubAgentTurns(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ("unused", false);
        });
        using var parentTurn = new CancellationTokenSource();
        var baseContext = await CreateContextAsync();
        var context = new SubAgentSessionContext
        {
            SessionService = baseContext.SessionService,
            ParentThread = baseContext.ParentThread,
            ParentTurnId = baseContext.ParentTurnId,
            ParentTurnCancellation = parentTurn.Token,
            RootThreadId = baseContext.RootThreadId
        };
        using var caller = new CancellationTokenSource();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect" },
            waitForCompletion: false,
            caller.Token);

        await caller.CancelAsync();
        await Task.Delay(100);
        Assert.Equal(TurnStatus.Running, (await _sessionService.GetThreadAsync(spawned.ChildThreadId)).Turns.Single().Status);

        await parentTurn.CancelAsync();
        await WaitUntilAsync(() =>
            _sessionService.GetThreadAsync(spawned.ChildThreadId).GetAwaiter().GetResult().Turns.Single().Status == TurnStatus.Cancelled);
    }

    [Fact]
    public async Task CloseAgent_ArchivesTargetSubtree()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        var grandchild = await _sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _tempDir,
                UserId = "user",
                ChannelName = SubAgentThreadOrigin.ChannelName,
                ChannelContext = child.Id
            },
            displayName: "Deeper",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = child.Id,
                ParentTurnId = "turn_child",
                RootThreadId = context.RootThreadId,
                Depth = 2,
                AgentPath = "/root/inspect/deeper",
                TaskName = "deeper",
                AgentNickname = "Deeper"
            }));
        await _sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = child.Id,
            ChildThreadId = grandchild.Id,
            ParentTurnId = "turn_child",
            Depth = 2,
            AgentPath = "/root/inspect/deeper",
            TaskName = "deeper",
            AgentNickname = "Deeper",
            Status = ThreadSpawnEdgeStatus.Open
        });

        await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);

        var incoming = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, incoming.Status);
        Assert.Equal(ThreadStatus.Archived, (await _sessionService.GetThreadAsync(child.Id)).Status);
        Assert.Equal(ThreadStatus.Archived, (await _sessionService.GetThreadAsync(grandchild.Id)).Status);
        Assert.Empty(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id));
    }

    [Fact]
    public async Task SpawnAgent_DefaultRole_PreservesParentPromptAndToolConfiguration()
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                MaxDepth = 1
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("default", result.AgentRole);
        Assert.Equal("default", child.Source.SubAgent?.AgentRole);
        Assert.Null(child.Configuration?.AgentControlToolAccess);
        Assert.Contains("Do not spawn additional agents", child.Configuration?.SubAgentRoleInstructions, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("all", true)]
    [InlineData("none", false)]
    [InlineData("2", false)]
    public async Task SpawnAgent_NativeFork_InheritsClientToolBindingsOnlyForFullHistory(
        string forkTurns,
        bool shouldInherit)
    {
        var context = await CreateContextAsync();
        _sessionService.ForkThreadToolBindingsHandler = (_, _) => true;

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = forkTurns
            },
            waitForCompletion: true,
            CancellationToken.None);

        if (shouldInherit)
        {
            var materialization = Assert.Single(_sessionService.MaterializedNativeSubAgentForks);
            Assert.Equal(context.ParentThread.Id, materialization.ParentThreadId);
            Assert.Equal(result.ChildThreadId, materialization.ChildThreadId);
            var fork = Assert.Single(_sessionService.ForkedThreadToolBindings);
            Assert.Equal(context.ParentThread.Id, fork.ParentThreadId);
            Assert.Equal(result.ChildThreadId, fork.ChildThreadId);
            Assert.Contains(result.ChildThreadId, _sessionService.RefreshedThreadAgents);
        }
        else
        {
            Assert.Empty(_sessionService.MaterializedNativeSubAgentForks);
            Assert.Empty(_sessionService.ForkedThreadToolBindings);
            Assert.DoesNotContain(result.ChildThreadId, _sessionService.RefreshedThreadAgents);
        }
    }

    [Fact]
    public async Task SpawnAgent_NestedFullHistoryFork_InheritsFromDirectParent()
    {
        var rootContext = await CreateContextAsync();
        _sessionService.ForkThreadToolBindingsHandler = (_, _) => true;
        var childResult = await SubAgentSessionControl.SpawnAgentAsync(
            rootContext,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "all",
                MaxDepth = 2
            },
            waitForCompletion: true,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(childResult.ChildThreadId);
        var childContext = new SubAgentSessionContext
        {
            SessionService = _sessionService,
            ParentThread = child,
            ParentTurnId = "turn-child",
            RootThreadId = rootContext.RootThreadId,
            Depth = 1
        };

        var grandchildResult = await SubAgentSessionControl.SpawnAgentAsync(
            childContext,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect nested code",
                TaskName = "nested",
                ForkTurns = "all",
                MaxDepth = 2
            },
            waitForCompletion: true,
            CancellationToken.None);

        Assert.Equal(2, _sessionService.ForkedThreadToolBindings.Count);
        Assert.Equal(
            (rootContext.ParentThread.Id, childResult.ChildThreadId),
            _sessionService.ForkedThreadToolBindings[0]);
        Assert.Equal(
            (childResult.ChildThreadId, grandchildResult.ChildThreadId),
            _sessionService.ForkedThreadToolBindings[1]);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("2")]
    public async Task SpawnAgent_PartialOrFreshContext_UsesConfiguredSubAgentModel(string forkTurns)
    {
        var context = await CreateContextAsync(new ThreadConfiguration { Model = "parent-model" });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = forkTurns,
                SubAgentPreference = SubAgentModelPreference.From(null, ModelPreferenceRules.CreateManual("subagent-model")),
                RuntimeConfig = AppConfigTestFactory.CreateOpenAI(model: "parent-model")
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("subagent-model", child.Configuration?.Model);
    }

    [Fact]
    public async Task SpawnAgent_NativeProfile_RoleModelOverridesConfiguredSubAgentModel()
    {
        var context = await CreateContextAsync(new ThreadConfiguration { Model = "parent-model" });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "none",
                RoleConfigs =
                [
                    new SubAgentRoleConfig
                    {
                        Name = SubAgentRoleNames.Default,
                        Model = "role-model"
                    }
                ],
                SubAgentPreference = SubAgentModelPreference.From(null, ModelPreferenceRules.CreateManual("subagent-model")),
                RuntimeConfig = AppConfigTestFactory.CreateOpenAI(model: "parent-model")
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("role-model", child.Configuration?.Model);
    }

    [Theory]
    [InlineData("invocation-model", null, "invocation-model", ModelReasoningEffort.High)]
    [InlineData(null, ModelReasoningEffort.Low, "role-model", ModelReasoningEffort.Low)]
    [InlineData("invocation-model", ModelReasoningEffort.Low, "invocation-model", ModelReasoningEffort.Low)]
    public async Task SpawnAgent_FreshContext_AppliesInvocationModelOverrideLast(
        string? invocationModel,
        ModelReasoningEffort? invocationEffort,
        string expectedModel,
        ModelReasoningEffort expectedEffort)
    {
        var context = await CreateContextAsync(new ThreadConfiguration
        {
            ProviderId = "openai",
            Model = "parent-model",
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = true,
                Effort = ModelReasoningEffort.High,
                Output = ReasoningOutput.Summary
            },
            Speed = InferenceSpeed.Fast,
        });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "none",
                RoleConfigs =
                [
                    new SubAgentRoleConfig
                    {
                        Name = SubAgentRoleNames.Default,
                        Model = "role-model"
                    }
                ],
                SubAgentPreference = new SubAgentModelPreference
                {
                    Model = "subagent-model",
                    Reasoning = new AppConfig.ReasoningConfig
                    {
                        Enabled = true,
                        Effort = ModelReasoningEffort.High,
                        Output = ReasoningOutput.Summary
                    },
                    Speed = InferenceSpeed.Fast,
                },
                InvocationModelOverride = new SubAgentInvocationModelOverride
                {
                    Model = invocationModel,
                    Effort = invocationEffort
                },
                RuntimeConfig = AppConfigTestFactory.CreateOpenAI(model: "parent-model")
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal(expectedModel, child.Configuration?.Model);
        Assert.True(child.Configuration?.Reasoning?.Enabled);
        Assert.Equal(expectedEffort, child.Configuration?.Reasoning?.Effort);
        Assert.Equal(ReasoningOutput.Summary, child.Configuration?.Reasoning?.Output);
        Assert.Equal(InferenceSpeed.Fast, child.Configuration?.Speed);
    }

    [Fact]
    public async Task SpawnAgent_FullHistory_InheritsParentModelAndReasoning()
    {
        var context = await CreateContextAsync(new ThreadConfiguration
        {
            Model = "parent-model",
                Reasoning = new AppConfig.ReasoningConfig { Effort = ModelReasoningEffort.High }
        });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ForkTurns = "all",
                RoleConfigs =
                [
                    new SubAgentRoleConfig
                    {
                        Name = SubAgentRoleNames.Default,
                        Model = "role-model"
                    }
                ],
                SubAgentPreference = new SubAgentModelPreference
                {
                    Model = "subagent-model",
                Reasoning = new AppConfig.ReasoningConfig { Effort = ModelReasoningEffort.Low }
                },
                RuntimeConfig = AppConfigTestFactory.CreateOpenAI(model: "parent-model")
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("parent-model", child.Configuration?.Model);
        Assert.Equal(ModelReasoningEffort.High, child.Configuration?.Reasoning?.Effort);
    }

    [Theory]
    [InlineData("worker", 1)]
    [InlineData("worker", 2)]
    [InlineData("explorer", 1)]
    public async Task SpawnAgent_BuiltInRoles_DoNotChangeParentToolConfiguration(string role, int maxDepth)
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "do the work",
                TaskName = role,
                AgentRole = role,
                MaxDepth = maxDepth
            },
            waitForCompletion: false,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal(role, result.AgentRole);
        Assert.Null(child.Configuration?.AgentControlToolAccess);
        Assert.Null(child.Configuration?.AllowedAgentControlTools);
        Assert.Empty(child.Configuration?.ToolAllowList ?? []);
    }

    [Fact]
    public async Task SpawnAgent_UnknownRole_FailsBeforeCreatingChildThread()
    {
        var context = await CreateContextAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                new SubAgentSpawnOptions
                {
                    AgentPrompt = "inspect code",
                    TaskName = "review",
                    AgentRole = "reviewer"
                },
                waitForCompletion: false,
                CancellationToken.None));

        Assert.Contains("Unknown subagent role 'reviewer'", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
    }

    [Fact]
    public async Task SpawnAgent_RejectsDepthBeyondLimit()
    {
        var context = await CreateContextAsync(depth: 1);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                new SubAgentSpawnOptions
                {
                    AgentPrompt = "spawn deeper",
                    TaskName = "deeper",
                    AgentRole = "worker",
                    MaxDepth = 1
                },
                waitForCompletion: false,
                CancellationToken.None));

        Assert.Contains("Subagent depth limit reached", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnAgent_WhenResidencyLimitReached_ArchivesOldestIdleSubAgentSubtree()
    {
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("ok", false)));
        var context = await CreateContextAsync();
        var now = DateTimeOffset.UtcNow;
        var older = await CreateIdleSubAgentAsync(context, "/root/older", "older", now.AddMinutes(-10));
        var deep = await CreateIdleSubAgentAsync(
            context,
            "/root/older/deep",
            "deep",
            now.AddMinutes(-9),
            parentThreadId: older.Id,
            depth: 2);
        var newer = await CreateIdleSubAgentAsync(context, "/root/newer", "newer", now.AddMinutes(-5));

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "fresh work",
                TaskName = "fresh",
                MaxConcurrentSubAgents = 2
            },
            waitForCompletion: true,
            CancellationToken.None);

        var openPaths = (await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id))
            .Select(edge => edge.AgentPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "/root/fresh", "/root/newer" }, openPaths);
        Assert.Equal(ThreadStatus.Archived, (await _sessionService.GetThreadAsync(older.Id)).Status);
        Assert.Equal(ThreadStatus.Archived, (await _sessionService.GetThreadAsync(deep.Id)).Status);
        Assert.Equal(ThreadStatus.Active, (await _sessionService.GetThreadAsync(newer.Id)).Status);
    }

    [Fact]
    public async Task SpawnAgent_WhenResidencyLimitReachedAndAllRunning_Throws()
    {
        ConfigureNativeSubAgentTurns(async (_, _, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return ("unused", false);
        });
        var context = await CreateContextAsync();

        var first = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "one", TaskName = "one", MaxConcurrentSubAgents = 2 },
            waitForCompletion: false,
            CancellationToken.None);
        var second = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "two", TaskName = "two", MaxConcurrentSubAgents = 2 },
            waitForCompletion: false,
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                new SubAgentSpawnOptions { AgentPrompt = "three", TaskName = "three", MaxConcurrentSubAgents = 2 },
                waitForCompletion: false,
                CancellationToken.None));

        Assert.Contains("concurrency limit reached", ex.Message, StringComparison.Ordinal);
        Assert.Equal(2, (await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id)).Count);

        await SubAgentSessionControl.CloseAgentAsync(_sessionService, first.ChildThreadId, CancellationToken.None);
        await SubAgentSessionControl.CloseAgentAsync(_sessionService, second.ChildThreadId, CancellationToken.None);
    }

    [Fact]
    public async Task SpawnAgent_WritesAgentPathAndRejectsDuplicateSiblingTaskName()
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect"
            },
            waitForCompletion: false,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));

        Assert.Equal("/root/inspect", result.AgentPath);
        Assert.Equal("inspect", result.TaskName);
        Assert.Equal("/root/inspect", child.Source.SubAgent?.AgentPath);
        Assert.Equal("inspect", child.Source.SubAgent?.TaskName);
        Assert.Equal("/root/inspect", edge.AgentPath);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SpawnAgentAsync(
                context,
                new SubAgentSpawnOptions
                {
                    AgentPrompt = "inspect again",
                    TaskName = "inspect"
                },
                waitForCompletion: false,
                CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BackgroundTaskStatus.Completed, false, false, "done")]
    [InlineData(BackgroundTaskStatus.Failed, true, false, "failed")]
    [InlineData(BackgroundTaskStatus.Stopped, false, true, null)]
    public async Task ChildTurnEnd_NotifiesParentInsteadOfPostingAMailboxEntry(
        string expectedStatus,
        bool isError,
        bool isCancelled,
        string? expectedResult)
    {
        var pending = new TaskCompletionSource<(string Text, bool IsError)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureNativeSubAgentTurns((_, _, ct) => pending.Task.WaitAsync(ct));
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect"
            },
            waitForCompletion: false,
            CancellationToken.None);
        if (isCancelled)
        {
            pending.SetCanceled();
        }
        else
        {
            pending.SetResult((expectedResult!, isError));
        }
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");
        var pendingMailbox = await _sessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            AgentPath.Root);

        Assert.Empty(pendingMailbox);
        Assert.Equal(BackgroundTaskKinds.Agent, ending.Kind);
        Assert.Equal(context.ParentThread.Id, ending.OwnerThreadId);
        Assert.Equal(expectedStatus, ending.Status);
        Assert.Equal(expectedResult, ending.Result);
        Assert.Equal(isCancelled, ending.StoppedByUser);
    }

    [Fact]
    public async Task FollowupTask_DeliversMailboxAndStartsTargetTurn()
    {
        var responseText = "first";
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult((responseText, false)));
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect"
            },
            waitForCompletion: true,
            CancellationToken.None);

        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        responseText = "followed";
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            CancellationToken.None);
        var waited = await _sessionService.WaitForTaskEndingAsync("/root/inspect", 2);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");

        Assert.Equal("running", followed.Status);
        Assert.Equal("followed", waited.Result);
        Assert.Empty(pending);
        Assert.Equal(2, child.Turns.Count);
        Assert.Contains("mailbox note", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("continue work", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Equal("subagentFollowupTask", child.Turns[1].Input?.AsUserMessage?.TriggerKind);
        Assert.Equal("Inspect", child.Turns[1].Input?.AsUserMessage?.TriggerLabel);
        Assert.Equal("/root/inspect", child.Turns[1].Input?.AsUserMessage?.TriggerRefId);
    }

    [Fact]
    public async Task FollowupTask_WhenTargetRunning_QueuesTurnInputAndMarksMailboxDelivered()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            CancellationToken.None);
        child = await _sessionService.GetThreadAsync(child.Id);
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");

        Assert.Equal("queued", followed.Status);
        Assert.Equal("/root/inspect", followed.AgentPath);
        Assert.Empty(pending);
        Assert.Single(child.Turns);
        var queued = Assert.Single(child.QueuedInputs);
        Assert.Equal("subagentFollowupTask", queued.TriggerKind);
        Assert.Equal("Inspect", queued.TriggerLabel);
        Assert.Equal("/root/inspect", queued.TriggerRefId);
        Assert.Equal("continue work", queued.DisplayText);
        var text = Assert.Single(queued.MaterializedInputParts).Text;
        Assert.Contains("mailbox note", text, StringComparison.Ordinal);
        Assert.Contains("continue work", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowupTask_WhenNativeTargetRunningAndDeliveryModeSteer_PromotesQueuedInputToGuidance()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            CancellationToken.None,
            deliveryMode: SubAgentFollowupDeliveryMode.Steer);
        child = await _sessionService.GetThreadAsync(child.Id);
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");

        Assert.Equal("guidancePending", followed.Status);
        Assert.Equal("/root/inspect", followed.AgentPath);
        Assert.Empty(pending);
        Assert.Single(child.Turns);
        var queued = Assert.Single(child.QueuedInputs);
        Assert.Equal("guidancePending", queued.Status);
        Assert.Equal("turn_active", queued.ReadyAfterTurnId);
        Assert.Equal("subagentFollowupTask", queued.TriggerKind);
        Assert.Equal("Inspect", queued.TriggerLabel);
        Assert.Equal("/root/inspect", queued.TriggerRefId);
        Assert.Equal("continue work", queued.DisplayText);
        var text = Assert.Single(queued.MaterializedInputParts).Text;
        Assert.Contains("mailbox note", text, StringComparison.Ordinal);
        Assert.Contains("continue work", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowupTask_WhenTargetIdleAndDeliveryModeSteer_StartsTargetTurn()
    {
        var responseText = "first";
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult((responseText, false)));
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect"
            },
            waitForCompletion: true,
            CancellationToken.None);

        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        responseText = "followed";
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            CancellationToken.None,
            deliveryMode: SubAgentFollowupDeliveryMode.Steer);
        var waited = await _sessionService.WaitForTaskEndingAsync("/root/inspect", 2);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Equal("running", followed.Status);
        Assert.Equal("followed", waited.Result);
        Assert.Empty(child.QueuedInputs);
        Assert.Equal(2, child.Turns.Count);
        Assert.Contains("mailbox note", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("continue work", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Equal("subagentFollowupTask", child.Turns[1].Input?.AsUserMessage?.TriggerKind);
    }

    [Fact]
    public async Task FollowupTask_WhenIdleDispatchFails_LeavesMailboxPending()
    {
        var context = await CreateContextAsync();
        await CreatePathSubAgentAsync(context);
        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        _sessionService.SubmitEventsHandler = (_, _) => throw new InvalidOperationException("dispatch failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.FollowupTaskAsync(
                context,
                "/root/inspect",
                "continue work",
                CancellationToken.None));

        var entry = Assert.Single(await _sessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            "/root/inspect"));
        Assert.Equal("mailbox note", entry.Message);
    }

    [Fact]
    public async Task FollowupTask_WhenQueuedTurnStarts_DeliversMaterializedPrompt()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");
        await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            CancellationToken.None);
        child = await _sessionService.GetThreadAsync(child.Id);
        var active = Assert.Single(child.Turns);
        active.Status = TurnStatus.Completed;
        active.CompletedAt = DateTimeOffset.UtcNow;
        await _store.SaveThreadAsync(child);

        await _sessionService.TryStartNextQueuedTurnAsync(child.Id, CancellationToken.None);

        var submitted = Assert.IsType<TextContent>(Assert.Single(_sessionService.LastSubmittedContent)).Text;
        Assert.Contains("mailbox note", submitted, StringComparison.Ordinal);
        Assert.Contains("continue work", submitted, StringComparison.Ordinal);
        child = await _sessionService.GetThreadAsync(child.Id);
        Assert.Empty(child.QueuedInputs);
        Assert.Equal("subagentFollowupTask", _sessionService.LastStartedQueuedInput?.TriggerKind);
        Assert.Equal("Inspect", _sessionService.LastStartedQueuedInput?.TriggerLabel);
        Assert.Equal("/root/inspect", _sessionService.LastStartedQueuedInput?.TriggerRefId);
    }

    [Fact]
    public async Task ListAgents_ReturnsRootAndOpenPathAgentsOnly()
    {
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect"
            },
            waitForCompletion: false,
            CancellationToken.None);
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "review code",
                TaskName = "review"
            },
            waitForCompletion: false,
            CancellationToken.None);
        await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);
        var legacyChild = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempDir,
            UserId = "user",
            ChannelName = SubAgentThreadOrigin.ChannelName,
            ChannelContext = context.ParentThread.Id
        });
        await _sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = context.ParentThread.Id,
            ChildThreadId = legacyChild.Id,
            ParentTurnId = "turn_legacy",
            Depth = 1,
            Status = ThreadSpawnEdgeStatus.Open
        });

        var listed = await SubAgentSessionControl.ListAgentsAsync(context, pathPrefix: null, CancellationToken.None);

        Assert.Equal(["/root", "/root/review"], listed.Data.Select(item => item.AgentPath).ToArray());
    }

    [Fact]
    public async Task ListAgents_ReportsCompletedChildFromLatestTurnStatus()
    {
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("ok", false)));
        var context = await CreateContextAsync();

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect"
            },
            waitForCompletion: true,
            CancellationToken.None);

        var listed = await SubAgentSessionControl.ListAgentsAsync(context, pathPrefix: null, CancellationToken.None);

        var child = Assert.Single(listed.Data, item => item.AgentPath == "/root/inspect");
        Assert.Equal("completed", child.Status);
    }

    [Fact]
    public async Task ColdResume_PreservesOpenChildConfigurationAndTypedPendingCommunication()
    {
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("initial", false)));
        var context = await CreateContextAsync(new ThreadConfiguration { Model = "stored-model" });
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspector",
                AgentRole = "recovery",
                RoleConfigs =
                [
                    new SubAgentRoleConfig
                    {
                        Name = "recovery",
                        Model = "stored-model",
                        ToolAllowList = ["ReadFile"],
                        AgentControlToolAccess = DotCraft.Tools.AgentControlToolAccess.AllowList,
                        AllowedAgentControlTools = [nameof(DotCraft.Tools.AgentTools.ListAgents)],
                        Instructions = "stored instructions"
                    }
                ],
                RuntimeConfig = AppConfigTestFactory.CreateOpenAI(model: "stored-model")
            },
            waitForCompletion: true,
            CancellationToken.None);
        await AddMailboxNoteAsync(context, "/root/inspect", "pending after restart");

        var restartedStore = new ThreadStore(_tempDir);
        var restartedService = new TestableSessionService(restartedStore);
        ConfigureNativeSubAgentTurns(restartedService, restartedStore, (_, _, _) => Task.FromResult(("resumed", false)));
        var restartedRoot = await restartedService.GetThreadAsync(context.RootThreadId);
        var restartedContext = new SubAgentSessionContext
        {
            SessionService = restartedService,
            ParentThread = restartedRoot,
            ParentTurnId = "turn_resume",
            RootThreadId = restartedRoot.Id,
            Depth = 0
        };
        var restoredChild = await restartedService.GetThreadAsync(spawned.ChildThreadId);
        var pending = Assert.Single(await restartedService.ListPendingSubAgentMailboxAsync(
            restartedRoot.Id,
            "/root/inspect"));

        Assert.Equal("/root/inspect", restoredChild.Source.SubAgent?.AgentPath);
        Assert.Equal("inspect", restoredChild.Source.SubAgent?.TaskName);
        Assert.Equal("Inspector", restoredChild.Source.SubAgent?.AgentNickname);
        Assert.Equal("recovery", restoredChild.Source.SubAgent?.AgentRole);
        Assert.Equal("stored-model", restoredChild.Configuration?.Model);
        Assert.Equal("stored instructions", restoredChild.Configuration?.SubAgentRoleInstructions);
        Assert.Equal(context.ParentTurnId, pending.ParentTurnId);

        await SubAgentSessionControl.FollowupTaskAsync(
            restartedContext,
            "/root/inspect",
            "resume work",
            CancellationToken.None);
        await restartedService.WaitForTaskEndingAsync("/root/inspect", 1);
        restoredChild = await restartedService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Empty(await restartedService.ListPendingSubAgentMailboxAsync(restartedRoot.Id, "/root/inspect"));
        var resumedInput = restoredChild.Turns[^1].Input?.AsUserMessage?.Text ?? string.Empty;
        Assert.Equal(1, resumedInput.Split("pending after restart", StringSplitOptions.None).Length - 1);

        await SubAgentSessionControl.CloseAgentAsync(restartedContext, "/root/inspect", CancellationToken.None);
        var secondRestart = new TestableSessionService(new ThreadStore(_tempDir));
        var secondRoot = await secondRestart.GetThreadAsync(restartedRoot.Id);
        var secondContext = new SubAgentSessionContext
        {
            SessionService = secondRestart,
            ParentThread = secondRoot,
            ParentTurnId = "turn_second_resume",
            RootThreadId = secondRoot.Id,
            Depth = 0
        };
        var closedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.FollowupTaskAsync(
                secondContext,
                "/root/inspect",
                "must stay closed",
                CancellationToken.None));
        Assert.Contains("closed", closedError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CloseAgent_UsesPathAndRejectsRootAndSelf()
    {
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect"
            },
            waitForCompletion: false,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var childContext = new SubAgentSessionContext
        {
            SessionService = _sessionService,
            ParentThread = child,
            ParentTurnId = "turn_child",
            RootThreadId = context.RootThreadId,
            Depth = 1
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.CloseAgentAsync(context, "/root", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.CloseAgentAsync(childContext, "/root/inspect", CancellationToken.None));

        var closed = await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));

        Assert.Equal(ThreadSpawnEdgeStatus.Closed, closed.Status);
        Assert.Equal("/root/inspect", closed.AgentPath);
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, edge.Status);
        Assert.Equal(ThreadStatus.Archived, (await _sessionService.GetThreadAsync(spawned.ChildThreadId)).Status);
    }

    [Fact]
    public async Task CloseAgent_WithNativeProfile_MarksProgressEntryCompleted()
    {
        var progressKey = $"CloseAgent-{Guid.NewGuid():N}";
        try
        {
            var context = await CreateContextAsync();
            await CreatePathSubAgentAsync(context, agentNickname: progressKey);

            await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);

            var progress = SubAgentProgressBridge.TryGet(progressKey);
            Assert.NotNull(progress);
            Assert.True(progress!.IsCompleted);
            Assert.Null(progress.CurrentTool);
            Assert.Null(progress.CurrentToolDisplay);
        }
        finally
        {
            SubAgentProgressBridge.Remove(progressKey);
        }
    }

    [Fact]
    public async Task SpawnAgent_ForkTurnsSelectsParentHistory()
    {
        var context = await CreateContextAsync();
        AddCompletedTurn(context.ParentThread, "turn_001", "first");
        AddCompletedTurn(context.ParentThread, "turn_002", "second");
        AddCompletedTurn(context.ParentThread, "turn_003", "third");

        var all = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "all history", TaskName = "all" },
            waitForCompletion: false,
            CancellationToken.None);
        var none = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "no history", TaskName = "none", ForkTurns = "none" },
            waitForCompletion: false,
            CancellationToken.None);
        var lastTwo = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "some history", TaskName = "last_two", ForkTurns = "2" },
            waitForCompletion: false,
            CancellationToken.None);

        var allThread = await _sessionService.GetThreadAsync(all.ChildThreadId);
        var noneThread = await _sessionService.GetThreadAsync(none.ChildThreadId);
        var lastTwoThread = await _sessionService.GetThreadAsync(lastTwo.ChildThreadId);

        Assert.Equal(["first", "second", "third"], allThread.Turns.Select(turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray());
        Assert.Empty(noneThread.Turns);
        Assert.Equal(["second", "third"], lastTwoThread.Turns.Select(turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task SpawnAgent_ForkedSplitToolResultsKeepResponsesCoverageAligned()
    {
        var context = await CreateContextAsync();
        AddCompletedToolTurn(context.ParentThread);

        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "continue", TaskName = "tool_history" },
            waitForCompletion: false,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var rawBaseline = child.Turns
            .SelectMany(ThreadStore.BuildModelVisibleHistoryFromTurn)
            .ToList();
        var providerHistory = new OpenAIResponsesProviderHistoryContext(
            new ProviderConversationIdentity(
                child.Id,
                child.Id,
                context.ParentThread.Id,
                ForkedFromThreadId: null,
                TurnId: "turn_child",
                ContextWindowId: "window_child",
                RequestKind: ProviderRequestKind.Turn,
                TurnStartedAtUnixMs: 1,
                ThreadSource: "subagent",
                SubagentKind: "native"),
            "openai",
            ProviderHistorySnapshot.Empty("window_child"),
            rawBaseline,
            appendAsync: null,
            replaceAsync: null,
            abortAsync: null);
        await providerHistory.ReplaceAsync(
            rawBaseline,
            options: null,
            "protocol_return",
            CancellationToken.None);
        var canonicalPrefix = providerHistory.CaptureSnapshot().Entries
            .Select(entry => entry.Item.GetRawText())
            .ToList();
        var preparedMessages = ModelRequestHistorySanitizer.Sanitize(
            [.. rawBaseline, new ChatMessage(ChatRole.User, "continue")]);

        var request = await providerHistory.PrepareInputAsync(
            preparedMessages,
            options: null,
            CancellationToken.None);

        Assert.True(rawBaseline.Count > preparedMessages.Count - 1);
        Assert.Equal(canonicalPrefix, request.Input
            .Take(canonicalPrefix.Count)
            .Select(item => item!.ToJsonString()));
        Assert.Equal(canonicalPrefix.Count + 1, request.Input.Count);
    }

    [Fact]
    public async Task SpawnAgent_ForkTurnsKeepsOnlyStableActiveTurnInput()
    {
        var context = await CreateContextAsync();
        AddActiveTurnWithUnstableItems(context.ParentThread, "current request");

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "clean active", TaskName = "clean" },
            waitForCompletion: false,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var forkedTurn = Assert.Single(child.Turns);

        Assert.Equal(TurnStatus.Completed, forkedTurn.Status);
        Assert.Equal("current request", forkedTurn.Input?.AsUserMessage?.Text);
        Assert.Single(forkedTurn.Items);
        Assert.Equal(ItemType.UserMessage, forkedTurn.Items.Single().Type);
    }

    private void ConfigureNativeSubAgentTurns(
        Func<string, IList<AIContent>, CancellationToken, Task<(string Text, bool IsError)>> run) =>
        ConfigureNativeSubAgentTurns(_sessionService, _store, run);

    private static void ConfigureNativeSubAgentTurns(
        TestableSessionService service,
        ThreadStore store,
        Func<string, IList<AIContent>, CancellationToken, Task<(string Text, bool IsError)>> run)
    {
        service.SubmitEventsHandler = (threadId, ct) =>
            RunNativeTurnAsync(service, store, threadId, service.LastSubmittedContent, run, ct);
    }

    private static async IAsyncEnumerable<SessionEvent> RunNativeTurnAsync(
        TestableSessionService service,
        ThreadStore store,
        string threadId,
        IReadOnlyList<AIContent> content,
        Func<string, IList<AIContent>, CancellationToken, Task<(string Text, bool IsError)>> run,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var thread = await service.GetThreadAsync(threadId, CancellationToken.None);
        var text = string.Concat(content.OfType<TextContent>().Select(item => item.Text));
        var triggerInfo = TurnTriggerScope.Current;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = thread.OriginChannel
        };
        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = text,
                TriggerKind = triggerInfo?.Kind,
                TriggerLabel = triggerInfo?.Label,
                TriggerRefId = triggerInfo?.RefId
            }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        await store.SaveThreadAsync(thread, CancellationToken.None);

        yield return new SessionEvent
        {
            ThreadId = threadId,
            TurnId = turn.Id,
            EventType = SessionEventType.TurnStarted,
            Payload = turn
        };

        string resultText = string.Empty;
        bool isError = false;
        bool cancelled = false;
        try
        {
            (resultText, isError) = await run(threadId, content.ToList(), ct);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        if (cancelled)
        {
            turn.Status = TurnStatus.Cancelled;
            turn.CompletedAt = DateTimeOffset.UtcNow;
            await store.SaveThreadAsync(thread, CancellationToken.None);
            yield return new SessionEvent
            {
                ThreadId = threadId,
                TurnId = turn.Id,
                EventType = SessionEventType.TurnCancelled,
                Payload = new TurnCancelledPayload { Turn = turn, Reason = "cancelled" }
            };
            yield break;
        }

        var responseItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
            TurnId = turn.Id,
            Type = isError ? ItemType.Error : ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = isError
                ? new ErrorPayload { Message = resultText, Code = "subagent_error", Fatal = true }
                : new AgentMessagePayload { Text = resultText }
        };
        turn.Items.Add(responseItem);
        turn.Status = isError ? TurnStatus.Failed : TurnStatus.Completed;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        turn.Error = isError ? resultText : null;
        await store.SaveThreadAsync(thread, CancellationToken.None);

        if (!isError)
        {
            yield return new SessionEvent
            {
                ThreadId = threadId,
                TurnId = turn.Id,
                ItemId = responseItem.Id,
                EventType = SessionEventType.ItemCompleted,
                Payload = responseItem
            };
        }

        yield return new SessionEvent
        {
            ThreadId = threadId,
            TurnId = turn.Id,
            EventType = isError ? SessionEventType.TurnFailed : SessionEventType.TurnCompleted,
            Payload = isError ? new TurnFailedPayload { Turn = turn, Error = resultText } : turn
        };
    }

    private static Task AddMailboxNoteAsync(SubAgentSessionContext context, string target, string message) =>
        context.SessionService.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = $"mailbox_{Guid.NewGuid():N}",
            RootThreadId = context.RootThreadId,
            SenderAgentPath = AgentPath.Root,
            TargetAgentPath = target,
            Message = message,
            ParentTurnId = context.ParentTurnId
        });

    private async Task<SubAgentSessionContext> CreateContextAsync(
        ThreadConfiguration? config = null,
        int depth = 0,
        Func<SubAgentLifecycleHookRequest, CancellationToken, Task>? lifecycleHook = null)
    {
        var parent = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempDir,
            UserId = "user",
            ChannelName = "desktop"
        }, config);

        return new SubAgentSessionContext
        {
            SessionService = _sessionService,
            ParentThread = parent,
            ParentTurnId = "turn_001",
            RootThreadId = parent.Id,
            Depth = depth,
            LifecycleHook = lifecycleHook
        };
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!await predicate())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private async Task<SessionThread> CreatePathSubAgentAsync(
        SubAgentSessionContext context,
        string agentNickname = "Inspect")
    {
        var child = await _sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _tempDir,
                UserId = "user",
                ChannelName = SubAgentThreadOrigin.ChannelName,
                ChannelContext = context.ParentThread.Id
            },
            displayName: agentNickname,
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = context.ParentThread.Id,
                ParentTurnId = context.ParentTurnId,
                RootThreadId = context.RootThreadId,
                Depth = context.Depth + 1,
                AgentPath = "/root/inspect",
                TaskName = "inspect",
                AgentNickname = agentNickname
            }));
        await _sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = context.ParentThread.Id,
            ChildThreadId = child.Id,
            ParentTurnId = context.ParentTurnId,
            Depth = context.Depth + 1,
            AgentPath = "/root/inspect",
            TaskName = "inspect",
            AgentNickname = agentNickname,
            Status = ThreadSpawnEdgeStatus.Open
        });

        return child;
    }

    private async Task<SessionThread> CreateIdleSubAgentAsync(
        SubAgentSessionContext context,
        string agentPath,
        string taskName,
        DateTimeOffset updatedAt,
        string? parentThreadId = null,
        int depth = 1)
    {
        var effectiveParentId = parentThreadId ?? context.ParentThread.Id;
        var child = await _sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _tempDir,
                UserId = "user",
                ChannelName = SubAgentThreadOrigin.ChannelName,
                ChannelContext = effectiveParentId
            },
            displayName: taskName,
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = effectiveParentId,
                ParentTurnId = "turn_seed",
                RootThreadId = context.RootThreadId,
                Depth = depth,
                AgentPath = agentPath,
                TaskName = taskName,
                AgentNickname = taskName
            }));
        await _sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = effectiveParentId,
            ChildThreadId = child.Id,
            ParentTurnId = "turn_seed",
            Depth = depth,
            AgentPath = agentPath,
            TaskName = taskName,
            AgentNickname = taskName,
            Status = ThreadSpawnEdgeStatus.Open,
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        });

        return child;
    }

    private static void AddCompletedTurn(SessionThread thread, string turnId, string text)
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(thread.Turns.Count);
        var turn = new SessionTurn
        {
            Id = turnId,
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now
        };
        var userItem = new SessionItem
        {
            Id = $"{turnId}_user",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = text }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
    }

    private static void AddCompletedToolTurn(SessionThread thread)
    {
        var now = DateTimeOffset.UtcNow;
        var turn = new SessionTurn
        {
            Id = "turn_tools",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            StartedAt = now,
            CompletedAt = now
        };
        var userItem = new SessionItem
        {
            Id = "turn_tools_user",
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = "inspect files" }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        for (var index = 1; index <= 2; index++)
        {
            turn.Items.Add(new SessionItem
            {
                Id = $"turn_tools_call_{index}",
                TurnId = turn.Id,
                Type = ItemType.ToolCall,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new ToolCallPayload
                {
                    CallId = $"call_{index}",
                    ToolName = "ReadFile",
                    ProviderFlatName = "ReadFile",
                    Arguments = new System.Text.Json.Nodes.JsonObject()
                }
            });
        }
        for (var index = 1; index <= 2; index++)
        {
            turn.Items.Add(new SessionItem
            {
                Id = $"turn_tools_result_{index}",
                TurnId = turn.Id,
                Type = ItemType.ToolResult,
                Status = ItemStatus.Completed,
                CreatedAt = now,
                CompletedAt = now,
                Payload = new ToolResultPayload
                {
                    CallId = $"call_{index}",
                    Result = $"result {index}"
                }
            });
        }
        thread.Turns.Add(turn);
    }

    private static void AddActiveTurnWithUnstableItems(SessionThread thread, string text)
    {
        var now = DateTimeOffset.UtcNow;
        var turn = new SessionTurn
        {
            Id = "turn_active",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = now
        };
        var userItem = new SessionItem
        {
            Id = "item_user",
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new UserMessagePayload { Text = text }
        };
        turn.Input = userItem;
        turn.Items.Add(userItem);
        turn.Items.Add(new SessionItem
        {
            Id = "item_tool",
            TurnId = turn.Id,
            Type = ItemType.ToolCall,
            Status = ItemStatus.Started,
            CreatedAt = now,
            Payload = new ToolCallPayload { CallId = "call_1", ToolName = "SpawnAgent" }
        });
        turn.Items.Add(new SessionItem
        {
            Id = "item_agent",
            TurnId = turn.Id,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Started,
            CreatedAt = now,
            Payload = new AgentMessagePayload { Text = "partial" }
        });
        thread.Turns.Add(turn);
    }
}
