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
        var pending = new TaskCompletionSource<(string Text, bool IsError)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureNativeSubAgentTurns(async (_, _, ct) =>
        {
            if (pending != null)
            {
                var current = pending;
                pending = null;
                return await current.Task.WaitAsync(ct);
            }
            return ("followed", false);
        });
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect", AgentNickname = "Inspect" },
            waitForCompletion: false,
            CancellationToken.None);

        var stopped = await Tasks().StopAsync(Caller(context), "inspect");

        Assert.Equal("/root/inspect", stopped.TaskId);
        Assert.Equal(BackgroundTaskKinds.Agent, stopped.Kind);
        Assert.Equal("Inspect", stopped.Label);
        Assert.Equal(BackgroundTaskStatus.Stopped, stopped.Status);
        Assert.Equal(ThreadSpawnEdgeStatus.Open, Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id)).Status);
        Assert.Empty(_sessionService.TaskEndings);

        var continued = await SubAgentSessionControl.SendAgentMessageAsync(context, "/root/inspect", "continue", CancellationToken.None);
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");

        Assert.Equal("running", continued.Status);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        Assert.Equal(new[] { TurnStatus.Cancelled, TurnStatus.Completed }, child.Turns.Select(turn => turn.Status));
        Assert.Equal(BackgroundTaskStatus.Completed, ending.Status);
        Assert.Equal("followed", ending.Result);
        Assert.False(ending.StoppedByUser);
    }

    [Fact]
    public async Task CloseFromClient_NotifiesTheParentThatTheUserStoppedTheChild()
    {
        var pending = new TaskCompletionSource<(string Text, bool IsError)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConfigureNativeSubAgentTurns((_, _, ct) => pending.Task.WaitAsync(ct));
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect" },
            waitForCompletion: false,
            CancellationToken.None);

        await SubAgentSessionControl.CloseAgentAsync(context, "/root/inspect", CancellationToken.None);
        var ending = await _sessionService.WaitForTaskEndingAsync("/root/inspect");

        Assert.Equal(BackgroundTaskStatus.Stopped, ending.Status);
        Assert.True(ending.StoppedByUser);
        Assert.True(ending.MayWake);
    }

    [Fact]
    public async Task SendMessage_ToARunningChild_SteersItsActiveTurn()
    {
        var context = await CreateContextAsync();
        var child = await CreatePathSubAgentAsync(context);
        AddActiveTurnWithUnstableItems(child, "active work");
        await _store.SaveThreadAsync(child);
        await AddMailboxNoteAsync(context, "/root/inspect", "mailbox note");

        var sent = await SubAgentSessionControl.SendAgentMessageAsync(context, "/root/inspect", "also check tests", CancellationToken.None);

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
        ConfigureNativeSubAgentTurns((_, _, _) => Task.FromResult(("all done", false)));
        var context = await CreateContextAsync();
        await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { AgentPrompt = "inspect code", TaskName = "inspect" },
            waitForCompletion: true,
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
