using DotCraft.Sessions;
using DotCraft.Text;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Commands;

public sealed class CommandBusyAndStopTests
{
    [Fact]
    public async Task Stop_CancelsTheActiveTurnThatBlockedCompactAndLeavesTheBackgroundShellAndQueuedInput()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var shell = await harness.StartBackgroundShellAsync(thread.Id);
        var turn = await harness.StartBlockedTurnAsync(thread.Id);
        var queued = await harness.Service.EnqueueTurnInputAsync(thread.Id, [new TextContent("later")]);

        Assert.Equal(FallbackText.CommandBusy, (await harness.ExecuteAsync(thread.Id, "/compact")).Message);
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
        Assert.Equal(2, after.TurnCount);
        Assert.Equal(BackgroundTerminalStatus.Killed, (await harness.Terminals.ReadAsync(shell.SessionId)).Status);
        var task = Assert.Single(await harness.Service.ListBackgroundTasksAsync(thread.Id));
        Assert.Equal(BackgroundTaskStatus.Stopped, task.Status);
    }

    [Fact]
    public async Task TasksStop_NumbersFollowStartOrderAndListedChoicesKeepTheirTask()
    {
        await using var harness = CommandSessionHarness.Create();
        var thread = await harness.CreateThreadAsync();
        var older = await harness.StartBackgroundShellAsync(thread.Id);
        var choice = Assert.Single((await harness.ExecuteAsync(thread.Id, "/tasks")).Choices!);
        var newer = await harness.StartBackgroundShellAsync(thread.Id);

        Assert.True((await harness.ExecuteAsync(thread.Id, "/tasks stop 2")).Handled);

        Assert.Equal(BackgroundTerminalStatus.Running, (await harness.Terminals.ReadAsync(older.SessionId)).Status);
        Assert.Equal(BackgroundTerminalStatus.Killed, (await harness.Terminals.ReadAsync(newer.SessionId)).Status);

        await harness.ExecuteAsync(thread.Id, choice.Command);

        Assert.Equal(BackgroundTerminalStatus.Killed, (await harness.Terminals.ReadAsync(older.SessionId)).Status);
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
}
