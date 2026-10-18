using DotCraft.Sessions;
using DotCraft.Text;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Commands;

public sealed class CommandBusyAndStopTests
{
    [Fact]
    public async Task Stop_CancelsTheActiveTurnAndLeavesTheBackgroundShellAndQueuedInput()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var shell = await harness.StartBackgroundShellAsync(thread.Id);
        var turn = await harness.StartBlockedTurnAsync(thread.Id);
        var queued = await harness.Service.EnqueueTurnInputAsync(thread.Id, [new TextContent("later")]);

        var result = await harness.ExecuteAsync(thread.Id, "/stop");

        Assert.True(result.Handled);
        var after = await harness.WaitForAsync(thread.Id, current =>
            current.Turns.Single(item => item.Id == turn.Id).Status == TurnStatus.Cancelled);
        Assert.True(
            after.QueuedInputs.Any(input => input.Id == queued.Id)
            || after.Turns.Any(item => item.Input?.AsUserMessage?.Text == "later"));
        Assert.Equal(BackgroundTerminalStatus.Running, (await harness.Terminals.ReadAsync(shell.SessionId)).Status);
    }

    [Fact]
    public async Task StopAll_CancelsTheTurnClearsQueuedInputAndStopsTheBackgroundShell()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var shell = await harness.StartBackgroundShellAsync(thread.Id);
        var turn = await harness.StartBlockedTurnAsync(thread.Id);
        await harness.Service.EnqueueTurnInputAsync(thread.Id, [new TextContent("later")]);

        var result = await harness.ExecuteAsync(thread.Id, "/stop all");

        Assert.True(result.Handled);
        var after = await harness.WaitForAsync(thread.Id, current =>
            current.Turns.Single(item => item.Id == turn.Id).Status == TurnStatus.Cancelled);
        await Task.Delay(300);
        after = await harness.Service.GetThreadAsync(thread.Id);
        Assert.Empty(after.QueuedInputs);
        Assert.Equal(2, after.Turns.Count);
        Assert.Equal(BackgroundTerminalStatus.Killed, (await harness.Terminals.ReadAsync(shell.SessionId)).Status);
        var task = Assert.Single(await harness.Service.ListBackgroundTasksAsync(thread.Id));
        Assert.Equal(BackgroundTaskStatus.Stopped, task.Status);
    }

    [Fact]
    public async Task TasksStop_StopsTheNumberedTaskFromTheList()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var shell = await harness.StartBackgroundShellAsync(thread.Id);

        var listed = await harness.ExecuteAsync(thread.Id, "/tasks");
        var choice = Assert.Single(listed.Choices!);
        Assert.Equal("/tasks stop 1", choice.Command);

        var stopped = await harness.ExecuteAsync(thread.Id, choice.Command);

        Assert.True(stopped.Handled);
        Assert.Equal(BackgroundTerminalStatus.Killed, (await harness.Terminals.ReadAsync(shell.SessionId)).Status);
        Assert.Null((await harness.ExecuteAsync(thread.Id, "/tasks")).Choices);
    }

    [Fact]
    public async Task New_InterruptsTheActiveTurnArchivesTheThreadAndSwitchesToANewOne()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var turn = await harness.StartBlockedTurnAsync(thread.Id);

        var result = await harness.ExecuteAsync(thread.Id, "/new");

        Assert.False(string.IsNullOrEmpty(result.ThreadId));
        Assert.NotEqual(thread.Id, result.ThreadId);
        var old = await harness.WaitForAsync(thread.Id, current =>
            current.Turns.Single(item => item.Id == turn.Id).Status == TurnStatus.Cancelled);
        Assert.Equal(ThreadStatus.Archived, old.Status);
        Assert.Equal(ThreadStatus.Active, (await harness.Service.GetThreadAsync(result.ThreadId!)).Status);
    }

    [Fact]
    public async Task Compact_IsRejectedWhileATurnIsActive()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var turn = await harness.StartBlockedTurnAsync(thread.Id);

        var result = await harness.ExecuteAsync(thread.Id, "/compact");

        Assert.Equal(FallbackText.CommandBusy, result.Message);
        var current = await harness.Service.GetThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Running, current.Turns.Single(item => item.Id == turn.Id).Status);
        await harness.ExecuteAsync(thread.Id, "/stop");
    }
}
