using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Protocol;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentSessionControlTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly TestableSessionService _sessionService;
    private static readonly SubAgentWaitAgentTimeoutOptions FastWaitTimeouts = new(
        MinTimeoutMs: 1,
        DefaultTimeoutMs: 50,
        MaxTimeoutMs: 10_000);

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
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task SpawnAgent_WithExternalProfile_CreatesChildThreadEdgeAndSyntheticTurn()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok", tokens: new SubAgentTokenUsage(3, 5));
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            result.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        var turn = Assert.Single(child.Turns);

        Assert.Equal("running", result.Status);
        Assert.Equal("cli-run", result.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, result.RuntimeType);
        Assert.False(result.SupportsSendInput);
        Assert.Equal("completed", waited.Status);
        Assert.Equal("cli ok", waited.Message);
        Assert.Equal("cli-run", child.Source.SubAgent?.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, child.Source.SubAgent?.RuntimeType);
        Assert.Equal("cli-run", edge.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, edge.RuntimeType);
        Assert.False(edge.SupportsSendInput);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal("inspect code", turn.Input?.AsUserMessage?.Text);
        Assert.Equal("subagentInput", turn.Input?.AsUserMessage?.TriggerKind);
        Assert.Equal("Inspect", turn.Input?.AsUserMessage?.TriggerLabel);
        Assert.Equal("/root/inspect", turn.Input?.AsUserMessage?.TriggerRefId);
        Assert.Equal("cli ok", turn.Items.Last().AsAgentMessage?.Text);
        Assert.Equal(3, turn.TokenUsage?.InputTokens);
        Assert.Equal(5, turn.TokenUsage?.OutputTokens);
    }

    [Fact]
    public async Task SpawnAgent_RunsSubagentStartAndStopLifecycleHooks()
    {
        var events = new ConcurrentQueue<(HookEvent Event, string? AgentPath, string? Status)>();
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync(lifecycleHook: (request, _) =>
        {
            events.Enqueue((
                request.Event,
                request.ChildThread.Source.SubAgent?.AgentPath,
                request.Status));
            return Task.CompletedTask;
        });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);
        await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            result.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        await WaitUntilAsync(() => events.Any(evt => evt.Event == HookEvent.SubagentStop));

        var observed = events.ToArray();
        Assert.Equal([HookEvent.SubagentStart, HookEvent.SubagentStop], observed.Select(evt => evt.Event).ToArray());
        Assert.All(observed, evt => Assert.Equal("/root/inspect", evt.AgentPath));
        Assert.Equal("running", observed[0].Status);
        Assert.Equal("completed", observed[1].Status);
    }

    [Fact]
    public async Task SpawnAgent_WithExternalProfile_PrependsRoleInstructionsToRuntimePrompt()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentRole = "explorer",
                ProfileName = "cli-run"
            },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        Assert.Contains("## SubAgent Role: explorer", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("You are an explorer SubAgent", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("## Task", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("inspect code", runtime.LastRequest?.Task, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendInput_WithExternalProfileWithoutResume_ReturnsUnsupported()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SendInputAsync(
                _sessionService,
                spawned.ChildThreadId,
                "continue",
                coordinator,
                CancellationToken.None));

        Assert.Contains("does not support SendInput", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendInput_WithResumableExternalProfile_AppendsSyntheticTurnAndUsesStoredSession()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok", resultSessionId: "sess-1");
        var store = new FakeExternalCliSessionStore();
        var coordinator = CreateCoordinator(runtime, supportsResume: true, resumeEnabled: true, store);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        runtime.ResultText = "continued";
        runtime.ResultSessionId = "sess-2";
        var sent = await SubAgentSessionControl.SendInputAsync(
            _sessionService,
            spawned.ChildThreadId,
            "continue",
            coordinator,
            CancellationToken.None);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            sent.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.True(sent.SupportsSendInput);
        Assert.Equal("continued", waited.Message);
        Assert.Equal("sess-1", runtime.LastLaunchContext?.ResumeSessionId);
        Assert.Equal(["sess-1", "sess-2"], store.RecordedSessionIds);
        Assert.Equal(2, child.Turns.Count);
        Assert.Equal("continue", child.Turns[1].Input?.AsUserMessage?.Text);
        Assert.Equal("subagentInput", child.Turns[1].Input?.AsUserMessage?.TriggerKind);
        Assert.Equal("Inspect", child.Turns[1].Input?.AsUserMessage?.TriggerLabel);
        Assert.Equal("/root/inspect", child.Turns[1].Input?.AsUserMessage?.TriggerRefId);
    }

    [Fact]
    public async Task SendInput_RunsSubagentStartAndStopLifecycleHooks()
    {
        var events = new ConcurrentQueue<(HookEvent Event, string? AgentPath, string? Status)>();
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok", resultSessionId: "sess-1");
        var store = new FakeExternalCliSessionStore();
        var coordinator = CreateCoordinator(runtime, supportsResume: true, resumeEnabled: true, store);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        runtime.ResultText = "continued";
        runtime.ResultSessionId = "sess-2";
        await SubAgentSessionControl.SendInputAsync(
            _sessionService,
            spawned.ChildThreadId,
            "continue",
            coordinator,
            (request, _) =>
            {
                events.Enqueue((
                    request.Event,
                    request.ChildThread.Source.SubAgent?.AgentPath,
                    request.Status));
                return Task.CompletedTask;
            },
            CancellationToken.None);
        await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        await WaitUntilAsync(() => events.Any(evt => evt.Event == HookEvent.SubagentStop));

        var observed = events.ToArray();
        Assert.Equal([HookEvent.SubagentStart, HookEvent.SubagentStop], observed.Select(evt => evt.Event).ToArray());
        Assert.All(observed, evt => Assert.Equal("/root/inspect", evt.AgentPath));
        Assert.Equal("running", observed[0].Status);
        Assert.Equal("completed", observed[1].Status);
    }

    [Fact]
    public async Task CloseAgent_WithRunningExternalProfile_CancelsSyntheticTurnClosesEdgeAndArchivesChild()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            WaitForCancellation = true
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var closed = await SubAgentSessionControl.CloseAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));

        Assert.Equal(ThreadSpawnEdgeStatus.Closed, closed.Status);
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, edge.Status);
        Assert.Equal(TurnStatus.Cancelled, child.Turns.Single().Status);
        Assert.Equal(ThreadStatus.Archived, child.Status);
        Assert.Empty(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id));
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
                AgentNickname = "Deeper",
                ProfileName = SubAgentCoordinator.DefaultProfileName,
                RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true
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
            ProfileName = SubAgentCoordinator.DefaultProfileName,
            RuntimeType = NativeSubAgentRuntime.RuntimeTypeName,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = true,
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
    public async Task WaitAgent_WhenTimeoutExpires_ReturnsTimeoutWithoutFailingChild()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            WaitForCancellation = true
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            timeoutSeconds: 1,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Equal("timeout", waited.Status);
        Assert.Equal("Inspect", waited.AgentNickname);
        Assert.Equal(TurnStatus.Running, child.Turns.Single().Status);

        await SubAgentSessionControl.CloseAgentAsync(_sessionService, spawned.ChildThreadId, CancellationToken.None);
    }

    [Fact]
    public async Task SpawnAgent_DefaultRole_DisablesAgentControlAndUsesLightPrompt()
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
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("default", result.AgentRole);
        Assert.Equal("default", child.Source.SubAgent?.AgentRole);
        Assert.Equal("subagent-light", child.Configuration?.PromptProfile);
        Assert.Equal(DotCraft.Tools.AgentControlToolAccess.Disabled, child.Configuration?.AgentControlToolAccess);
        Assert.Contains("Do not spawn additional agents", child.Configuration?.RoleInstructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SpawnAgent_NativeProfile_UsesConfiguredSubAgentModel()
    {
        var context = await CreateContextAsync(new ThreadConfiguration { Model = "parent-model" });

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                SubAgentModel = "subagent-model"
            },
            waitForCompletion: false,
            coordinator: null,
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
                RoleConfigs =
                [
                    new SubAgentRoleConfig
                    {
                        Name = SubAgentRoleNames.Default,
                        Model = "role-model"
                    }
                ],
                SubAgentModel = "subagent-model"
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("role-model", child.Configuration?.Model);
    }

    [Fact]
    public async Task SpawnAgent_WorkerRole_DefaultMaxDepth_RemovesSpawnAgent()
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "implement change",
                TaskName = "worker",
                AgentRole = "worker",
                MaxDepth = 1
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal("worker", result.AgentRole);
        Assert.Equal(DotCraft.Tools.AgentControlToolAccess.AllowList, child.Configuration?.AgentControlToolAccess);
        Assert.DoesNotContain(nameof(DotCraft.Tools.AgentTools.SpawnAgent), child.Configuration?.AllowedAgentControlTools ?? []);
        Assert.Contains(nameof(DotCraft.Tools.AgentTools.WaitAgent), child.Configuration?.AllowedAgentControlTools ?? []);
    }

    [Fact]
    public async Task SpawnAgent_WorkerRole_WhenDepthAllows_KeepsFullAgentControl()
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "implement change",
                TaskName = "worker",
                AgentRole = "worker",
                MaxDepth = 2
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);

        Assert.Equal(DotCraft.Tools.AgentControlToolAccess.Full, child.Configuration?.AgentControlToolAccess);
        Assert.Null(child.Configuration?.AllowedAgentControlTools);
    }

    [Fact]
    public async Task SpawnAgent_ExplorerRole_UsesReadOnlyToolAllowList()
    {
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "map the code",
                TaskName = "explore",
                AgentRole = "explorer"
            },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var allowList = child.Configuration?.ToolAllowList ?? [];

        Assert.Contains("ReadFile", allowList);
        Assert.Contains("WebSearch", allowList);
        Assert.DoesNotContain("WriteFile", allowList);
        Assert.DoesNotContain("Exec", allowList);
        Assert.Equal(DotCraft.Tools.AgentControlToolAccess.Disabled, child.Configuration?.AgentControlToolAccess);
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
                coordinator: null,
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
                coordinator: null,
                CancellationToken.None));

        Assert.Contains("Subagent depth limit reached", ex.Message, StringComparison.Ordinal);
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
            coordinator: null,
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
                coordinator: null,
                CancellationToken.None));

        Assert.Contains("already exists", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendMessage_WritesMailboxWithoutStartingTargetTurnAndWakesWaitAgent()
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
            coordinator: null,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var beforeTurnCount = child.Turns.Count;
        var waitTask = SubAgentSessionControl.WaitAgentAsync(
            context,
            timeoutMs: 1000,
            CancellationToken.None,
            FastWaitTimeouts);

        var sent = await SubAgentSessionControl.SendMessageAsync(
            context,
            "/root/inspect",
            "please note this",
            CancellationToken.None);
        var waited = await waitTask;
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");
        child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Equal("sent", sent.Status);
        Assert.Equal("/root/inspect", sent.AgentPath);
        Assert.Equal("changed", waited.Status);
        Assert.False(waited.TimedOut);
        var entry = Assert.Single(pending);
        Assert.Equal(AgentPath.Root, entry.SenderAgentPath);
        Assert.Equal("/root/inspect", entry.TargetAgentPath);
        Assert.Equal("please note this", entry.Message);
        Assert.Equal(beforeTurnCount, child.Turns.Count);
    }

    [Fact]
    public async Task WaitAgent_WhenChildTurnCompletes_ReturnsChanged()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "later")
        {
            PendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);
        var waitTask = SubAgentSessionControl.WaitAgentAsync(
            context,
            timeoutMs: 1000,
            CancellationToken.None,
            FastWaitTimeouts);

        runtime.PendingResult.SetResult(new DotCraft.Agents.SubAgentRunResult { Text = "done" });
        var waited = await waitTask;
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(
            context.RootThreadId,
            AgentPath.Root);

        Assert.Equal("changed", waited.Status);
        Assert.False(waited.TimedOut);
        var entry = Assert.Single(pending);
        Assert.Equal("/root/inspect", entry.SenderAgentPath);
        Assert.Equal(AgentPath.Root, entry.TargetAgentPath);
        Assert.Contains("<subagent_notification>", entry.Message, StringComparison.Ordinal);
        Assert.Contains("\"agentPath\":\"/root/inspect\"", entry.Message, StringComparison.Ordinal);
        Assert.Contains("\"completed\":\"done\"", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAgent_WhenMailboxAlreadyPending_ReturnsChanged()
    {
        var context = await CreateContextAsync();
        await _sessionService.AddSubAgentMailboxEntryAsync(new SubAgentMailboxEntry
        {
            Id = "mailbox_existing",
            RootThreadId = context.RootThreadId,
            SenderAgentPath = "/root/inspect",
            TargetAgentPath = AgentPath.Root,
            Message = "ready",
            Status = SubAgentMailboxStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        });

        var waited = await SubAgentSessionControl.WaitAgentAsync(
            context,
            timeoutMs: 10_000,
            CancellationToken.None,
            FastWaitTimeouts);

        Assert.Equal("changed", waited.Status);
        Assert.False(waited.TimedOut);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(14_999)]
    public async Task WaitAgent_WhenTimeoutMsIsBelowConfiguredMin_Throws(int timeoutMs)
    {
        var context = await CreateContextAsync();

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SubAgentSessionControl.WaitAgentAsync(context, timeoutMs, CancellationToken.None));

        Assert.Equal("timeoutMs", ex.ParamName);
        Assert.Contains("at least 15000", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAgent_WhenTimeoutMsIsAboveConfiguredMax_Throws()
    {
        var context = await CreateContextAsync();
        var options = new SubAgentWaitAgentTimeoutOptions(
            MinTimeoutMs: 1,
            DefaultTimeoutMs: 50,
            MaxTimeoutMs: 100);

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SubAgentSessionControl.WaitAgentAsync(
                context,
                timeoutMs: 101,
                CancellationToken.None,
                options));

        Assert.Equal("timeoutMs", ex.ParamName);
        Assert.Contains("at most 100", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitAgent_WhenTimeoutMsIsOmitted_UsesConfiguredDefault()
    {
        var context = await CreateContextAsync();
        var options = new SubAgentWaitAgentTimeoutOptions(
            MinTimeoutMs: 1,
            DefaultTimeoutMs: 1,
            MaxTimeoutMs: 100);

        var waited = await SubAgentSessionControl.WaitAgentAsync(
            context,
            timeoutMs: null,
            CancellationToken.None,
            options);

        Assert.Equal("timeout", waited.Status);
        Assert.True(waited.TimedOut);
    }

    [Fact]
    public async Task WaitAgent_WhenZeroTimeoutIsConfiguredAndRequested_ReturnsTimeout()
    {
        var context = await CreateContextAsync();
        var options = new SubAgentWaitAgentTimeoutOptions(
            MinTimeoutMs: 0,
            DefaultTimeoutMs: 0,
            MaxTimeoutMs: 0);

        var waited = await SubAgentSessionControl.WaitAgentAsync(
            context,
            timeoutMs: 0,
            CancellationToken.None,
            options);

        Assert.Equal("timeout", waited.Status);
        Assert.True(waited.TimedOut);
    }

    [Fact]
    public async Task FollowupTask_DeliversMailboxAndStartsTargetTurn()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "first", resultSessionId: "sess-1");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        runtime.ResultText = "followed";
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator,
            CancellationToken.None);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            followed.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");

        Assert.Equal("running", followed.Status);
        Assert.Equal("followed", waited.Message);
        Assert.Empty(pending);
        Assert.Equal(2, child.Turns.Count);
        Assert.Contains("mailbox note", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("continue work", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Equal("subagentFollowupTask", child.Turns[1].Input?.AsUserMessage?.TriggerKind);
        Assert.Equal("Inspect", child.Turns[1].Input?.AsUserMessage?.TriggerLabel);
        Assert.Equal("/root/inspect", child.Turns[1].Input?.AsUserMessage?.TriggerRefId);
        Assert.Contains("mailbox note", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("continue work", runtime.LastRequest?.Task, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FollowupTask_WhenTargetRunning_QueuesTurnInputAndMarksMailboxDelivered()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator: null,
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

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator: null,
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
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "first", resultSessionId: "sess-1");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        runtime.ResultText = "followed";
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator,
            CancellationToken.None,
            deliveryMode: SubAgentFollowupDeliveryMode.Steer);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            followed.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Equal("running", followed.Status);
        Assert.Equal("followed", waited.Message);
        Assert.Empty(child.QueuedInputs);
        Assert.Equal(2, child.Turns.Count);
        Assert.Contains("mailbox note", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("continue work", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Equal("subagentFollowupTask", child.Turns[1].Input?.AsUserMessage?.TriggerKind);
    }

    [Fact]
    public async Task FollowupTask_WhenExternalTargetRunningAndDeliveryModeSteer_RejectsWithoutQueueingOrDeliveringMailbox()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(
            context,
            runtimeType: CliOneshotRuntime.RuntimeTypeName,
            profileName: "cli-run");
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.FollowupTaskAsync(
                context,
                "/root/inspect",
                "continue work",
                coordinator: null,
                CancellationToken.None,
                deliveryMode: SubAgentFollowupDeliveryMode.Steer));
        child = await _sessionService.GetThreadAsync(child.Id);
        var pending = await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect");

        Assert.Contains("running native SubAgents", ex.Message, StringComparison.Ordinal);
        Assert.Empty(child.QueuedInputs);
        var entry = Assert.Single(pending);
        Assert.Equal("mailbox note", entry.Message);
    }

    [Fact]
    public async Task FollowupTask_WhenQueuedTurnStarts_DeliversMaterializedPrompt()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator: null,
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
    public async Task FollowupTask_WhenExternalTargetRunning_RunsQueuedFollowupAfterActiveTurnCompletes()
    {
        var pendingRun = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            PendingResult = pendingRun
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        await SubAgentSessionControl.SendMessageAsync(context, "/root/inspect", "mailbox note", CancellationToken.None);
        var followed = await SubAgentSessionControl.FollowupTaskAsync(
            context,
            "/root/inspect",
            "continue work",
            coordinator,
            CancellationToken.None);
        pendingRun.SetResult(new DotCraft.Agents.SubAgentRunResult { Text = "done" });
        await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        Assert.Equal("queued", followed.Status);
        Assert.Empty(child.QueuedInputs);
        Assert.Equal(2, child.Turns.Count);
        Assert.Contains("mailbox note", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("continue work", child.Turns[1].Input?.AsUserMessage?.Text, StringComparison.Ordinal);
        Assert.Contains("mailbox note", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("continue work", runtime.LastRequest?.Task, StringComparison.Ordinal);
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
            coordinator: null,
            CancellationToken.None);
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "review code",
                TaskName = "review"
            },
            waitForCompletion: false,
            coordinator: null,
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
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        var listed = await SubAgentSessionControl.ListAgentsAsync(context, pathPrefix: null, CancellationToken.None);

        var child = Assert.Single(listed.Data, item => item.AgentPath == "/root/inspect");
        Assert.Equal("completed", child.Status);
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
            coordinator: null,
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
        SubAgentProgressBridge.Remove("Inspect");
        try
        {
            var context = await CreateContextAsync();
            await CreatePathSubAgentAsync(context);

            await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);

            var progress = SubAgentProgressBridge.TryGet("Inspect");
            Assert.NotNull(progress);
            Assert.True(progress!.IsCompleted);
            Assert.Null(progress.CurrentTool);
            Assert.Null(progress.CurrentToolDisplay);
        }
        finally
        {
            SubAgentProgressBridge.Remove("Inspect");
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
            coordinator: null,
            CancellationToken.None);
        var none = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "no history", TaskName = "none", ForkTurns = "none" },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);
        var lastTwo = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "some history", TaskName = "last_two", ForkTurns = "2" },
            waitForCompletion: false,
            coordinator: null,
            CancellationToken.None);

        var allThread = await _sessionService.GetThreadAsync(all.ChildThreadId);
        var noneThread = await _sessionService.GetThreadAsync(none.ChildThreadId);
        var lastTwoThread = await _sessionService.GetThreadAsync(lastTwo.ChildThreadId);

        Assert.Equal(["first", "second", "third"], allThread.Turns.Select(turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray());
        Assert.Empty(noneThread.Turns);
        Assert.Equal(["second", "third"], lastTwoThread.Turns.Select(turn => turn.Input?.AsUserMessage?.Text ?? string.Empty).ToArray());
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
            coordinator: null,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var forkedTurn = Assert.Single(child.Turns);

        Assert.Equal(TurnStatus.Completed, forkedTurn.Status);
        Assert.Equal("current request", forkedTurn.Input?.AsUserMessage?.Text);
        Assert.Single(forkedTurn.Items);
        Assert.Equal(ItemType.UserMessage, forkedTurn.Items.Single().Type);
    }

    [Fact]
    public async Task SpawnAgent_ExternalProfileRendersForkContextIntoRuntimePrompt()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        AddCompletedTurn(context.ParentThread, "turn_001", "parent context");

        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                AgentPrompt = "inspect code",
                TaskName = "inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        Assert.Contains("## Parent Context", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("parent context", runtime.LastRequest?.Task, StringComparison.Ordinal);
        Assert.Contains("inspect code", runtime.LastRequest?.Task, StringComparison.Ordinal);
    }

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

    private async Task<SessionThread> CreatePathSubAgentAsync(
        SubAgentSessionContext context,
        string runtimeType = NativeSubAgentRuntime.RuntimeTypeName,
        string profileName = SubAgentCoordinator.DefaultProfileName)
    {
        var child = await _sessionService.CreateThreadAsync(
            new SessionIdentity
            {
                WorkspacePath = _tempDir,
                UserId = "user",
                ChannelName = SubAgentThreadOrigin.ChannelName,
                ChannelContext = context.ParentThread.Id
            },
            displayName: "Inspect",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = context.ParentThread.Id,
                ParentTurnId = context.ParentTurnId,
                RootThreadId = context.RootThreadId,
                Depth = context.Depth + 1,
                AgentPath = "/root/inspect",
                TaskName = "inspect",
                AgentNickname = "Inspect",
                ProfileName = profileName,
                RuntimeType = runtimeType,
                SupportsSendMessage = true,
                SupportsFollowupTask = true,
                SupportsClose = true
            }));
        await _sessionService.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = context.ParentThread.Id,
            ChildThreadId = child.Id,
            ParentTurnId = context.ParentTurnId,
            Depth = context.Depth + 1,
            AgentPath = "/root/inspect",
            TaskName = "inspect",
            AgentNickname = "Inspect",
            ProfileName = profileName,
            RuntimeType = runtimeType,
            SupportsSendMessage = true,
            SupportsFollowupTask = true,
            SupportsClose = true,
            Status = ThreadSpawnEdgeStatus.Open
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

    private SubAgentCoordinator CreateCoordinator(
        FakeRuntime runtime,
        bool supportsResume,
        bool resumeEnabled,
        IExternalCliSessionStore? store = null)
    {
        return new SubAgentCoordinator(
            _tempDir,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli",
                    InputMode = "arg",
                    OutputFormat = "text",
                    SupportsResume = supportsResume,
                    ResumeArgTemplate = supportsResume ? "--resume {sessionId}" : null,
                    ResumeSessionIdJsonPath = supportsResume ? "session_id" : null
                }
            ],
            externalCliSessionStore: store,
            enableExternalCliSessionResume: resumeEnabled);
    }

    private sealed class FakeRuntime(
        string runtimeType,
        string resultText,
        SubAgentTokenUsage? tokens = null,
        string? resultSessionId = null) : ISubAgentRuntime
    {
        public string RuntimeType { get; } = runtimeType;

        public string ResultText { get; set; } = resultText;

        public string? ResultSessionId { get; set; } = resultSessionId;

        public bool WaitForCancellation { get; init; }

        public TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>? PendingResult { get; init; }

        public SubAgentLaunchContext? LastLaunchContext { get; private set; }

        public SubAgentTaskRequest? LastRequest { get; private set; }

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken)
        {
            LastLaunchContext = context;
            return Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));
        }

        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (PendingResult != null)
                return PendingResult.Task.WaitAsync(cancellationToken);
            if (WaitForCancellation)
                return WaitForCancellationAsync(cancellationToken);

            return Task.FromResult(new DotCraft.Agents.SubAgentRunResult
            {
                Text = ResultText,
                TokensUsed = tokens,
                SessionId = ResultSessionId
            });
        }

        private async Task<DotCraft.Agents.SubAgentRunResult> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Expected cancellation.");
        }

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeExternalCliSessionStore : IExternalCliSessionStore
    {
        private readonly Dictionary<string, ExternalCliStoredSession> _sessions = new(StringComparer.Ordinal);

        public List<string> RecordedSessionIds { get; } = [];

        public bool TryGetResumeSession(
            string profileName,
            string? label,
            string workingDirectory,
            out ExternalCliStoredSession session)
        {
            var key = BuildKey(profileName, label, workingDirectory);
            return _sessions.TryGetValue(key, out session!);
        }

        public void RecordSuccessfulRun(
            string profileName,
            string? label,
            string workingDirectory,
            string sessionId)
        {
            RecordedSessionIds.Add(sessionId);
            _sessions[BuildKey(profileName, label, workingDirectory)] =
                new ExternalCliStoredSession(profileName, label, workingDirectory, sessionId);
        }

        private static string BuildKey(string profileName, string? label, string workingDirectory) =>
            $"{profileName}\n{label}\n{workingDirectory}";
    }
}
