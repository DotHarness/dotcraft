using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using SessionIdentity = DotCraft.Sessions.SessionIdentity;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SubAgentSessionControlTests
{
    [Fact]
    public async Task TaskStop_InterruptsARunningChildWithoutANotificationAndSendMessageContinuesIt()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "followed")
        {
            PendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var stopped = await Tasks().StopAsync(Caller(context), "inspect");

        Assert.Equal("/root/inspect", stopped.TaskId);
        Assert.Equal(BackgroundTaskKinds.Agent, stopped.Kind);
        Assert.Equal("Inspect", stopped.Label);
        Assert.Equal(BackgroundTaskStatus.Stopped, stopped.Status);
        Assert.Equal(ThreadSpawnEdgeStatus.Open, Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id)).Status);
        Assert.Empty(_sessionService.TaskEndings);

        runtime.PendingResult = null;
        var continued = await SubAgentSessionControl.SendAgentMessageAsync(context, "/root/inspect", "continue", coordinator, CancellationToken.None);
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");

        Assert.Equal("running", continued.Status);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        Assert.Equal(new[] { TurnStatus.Cancelled, TurnStatus.Completed }, child.Turns.Select(turn => turn.Status));
        Assert.Equal(BackgroundTaskStatus.Completed, ending.Status);
        Assert.Equal("followed", ending.Result);
        Assert.False(ending.StoppedByUser);
    }

    [Fact]
    public async Task TaskStop_DrainsExternalCancellationBeforeReportingStopped()
    {
        var cancellationGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            PendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
                TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationGate = cancellationGate
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var stopping = Tasks().StopAsync(Caller(context), "inspect");
        await runtime.CancellationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(stopping.IsCompleted);
        Assert.Equal(BackgroundTaskStatus.Running, (await Tasks().FindAsync(Caller(context), "inspect")).Status);

        cancellationGate.SetResult();
        var stopped = await stopping;

        Assert.Equal(BackgroundTaskStatus.Stopped, stopped.Status);
        Assert.Empty(_sessionService.TaskEndings);
    }

    [Fact]
    public async Task InterruptFromClient_StopsAnExternalChildAsTheUserAndLeavesItOpen()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            PendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        Assert.True(SubAgentSessionControl.InterruptRunningChild(spawned.ChildThreadId));
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");

        Assert.Equal(BackgroundTaskStatus.Stopped, ending.Status);
        Assert.True(ending.StoppedByUser);
        Assert.True(ending.MayWake);
        Assert.Equal(ThreadSpawnEdgeStatus.Open, Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id)).Status);
    }

    [Fact]
    public async Task CloseFromClient_NotifiesTheParentThatTheUserStoppedTheChild()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            PendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(
                TaskCreationOptions.RunContinuationsAsynchronously)
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");

        Assert.Equal(BackgroundTaskStatus.Stopped, ending.Status);
        Assert.True(ending.StoppedByUser);
        Assert.True(ending.MayWake);
    }

    [Fact]
    public async Task ArchiveIdleParent_InterruptsAndArchivesRunningExternalChild()
    {
        var timeout = TimeSpan.FromSeconds(10);
        var releaseCancellation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingResult = new TaskCompletionSource<DotCraft.Agents.SubAgentRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            PendingResult = pendingResult,
            CancellationGate = releaseCancellation
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        await using var factory = new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: AppConfigTestFactory.CreateOpenAI(),
            memoryStore: new MemoryStore(_tempDir),
            skillsLoader: new SkillsLoader(_tempDir),
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            chatClientRegistry: TestModelProviderRegistry.Create());
        await using var service = new SessionService(
            factory, factory.CreateDefaultAgent(), new SessionPersistenceService(_store), new SessionGate());
        var parent = await service.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempDir,
            UserId = "user",
            ChannelName = "desktop"
        });
        var childStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = new SubAgentSessionContext
        {
            SessionService = service,
            ParentThread = parent,
            ParentTurnId = "turn_001",
            RootThreadId = parent.Id,
            LifecycleHook = (request, _) =>
            {
                if (request.Event == HookEvent.SubagentStop)
                    childStopped.TrySetResult();
                return Task.CompletedTask;
            }
        };
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        try
        {
            await service.ArchiveThreadAsync(parent.Id).WaitAsync(timeout);
            await runtime.CancellationStarted.Task.WaitAsync(timeout);
            Assert.Equal(ThreadStatus.Archived, (await service.ReadThreadSnapshotAsync(spawned.ChildThreadId)).Thread.Status);

            releaseCancellation.SetResult();
            await childStopped.Task.WaitAsync(timeout);
            var child = await service.GetThreadAsync(spawned.ChildThreadId);
            Assert.Equal(TurnStatus.Cancelled, Assert.Single(child.Turns).Status);
        }
        finally
        {
            releaseCancellation.TrySetResult();
            pendingResult.TrySetResult(new DotCraft.Agents.SubAgentRunResult { Text = "unused" });
        }
    }

    [Fact]
    public async Task SendMessage_ToARunningNativeChild_SteersItsActiveTurn()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);
        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");

        var sent = await SubAgentSessionControl.SendAgentMessageAsync(context, "/root/inspect", "also check tests", coordinator: null, CancellationToken.None);

        child = await _sessionService.GetThreadAsync(child.Id);
        Assert.Equal("guidancePending", sent.Status);
        var queued = Assert.Single(child.QueuedInputs);
        Assert.Equal("guidancePending", queued.Status);
        Assert.Equal("turn_active", queued.ReadyAfterTurnId);
        var text = Assert.Single(queued.MaterializedInputParts).Text;
        Assert.Contains("mailbox note", text, StringComparison.Ordinal);
        Assert.Contains("also check tests", text, StringComparison.Ordinal);
        Assert.Empty(await _sessionService.ListPendingSubAgentMailboxAsync(context.RootThreadId, "/root/inspect"));
    }

    [Fact]
    public async Task TaskStop_OnAFinishedChild_ReturnsItsFinalStatusAndLastMessage()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "all done");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        var result = await Tasks().StopAsync(Caller(context), "/root/inspect");

        Assert.Equal(BackgroundTaskStatus.Completed, result.Status);
        Assert.Equal("all done", result.LastAgentMessage);
    }

    [Fact]
    public async Task TaskStop_AcceptsOnlyTheCallersDescendants()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        var otherRoot = await CreateContextAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Tasks().StopAsync(new BackgroundTaskCaller(child, context.RootThreadId), "/root/inspect"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Tasks().StopAsync(new BackgroundTaskCaller(child, context.RootThreadId), "/root"));
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Tasks().StopAsync(Caller(otherRoot), "/root/inspect"));
        Assert.Equal(BackgroundTaskStatus.Completed, (await Tasks().StopAsync(Caller(context), "inspect")).Status);
    }

    private BackgroundTaskRegistry Tasks() => new([new SubAgentTaskKind(_sessionService)]);

    private static BackgroundTaskCaller Caller(SubAgentSessionContext context) =>
        new(context.ParentThread, context.RootThreadId);
}
