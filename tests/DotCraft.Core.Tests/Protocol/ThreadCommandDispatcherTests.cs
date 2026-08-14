using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadCommandDispatcherTests
{
    [Fact]
    public async Task Commands_AreExecutedInSubmissionOrder()
    {
        await using var dispatcher = new ThreadCommandDispatcher("thread_test");
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<int>();

        var first = dispatcher.InvokeAsync(
            async _ =>
            {
                order.Add(1);
                firstEntered.SetResult();
                await releaseFirst.Task;
                order.Add(2);
            });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var second = dispatcher.InvokeAsync(
            _ =>
            {
                order.Add(3);
                return Task.CompletedTask;
            });

        await Task.Delay(50);
        Assert.Equal([1], order);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public async Task Command_CapturesCallersExecutionContext()
    {
        await using var dispatcher = new ThreadCommandDispatcher("thread_context");
        var ambient = new AsyncLocal<string?>();
        var blockerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var blocker = dispatcher.InvokeAsync(
            async _ =>
            {
                blockerEntered.SetResult();
                await releaseBlocker.Task;
            });
        await blockerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        ambient.Value = "captured";
        var captured = dispatcher.InvokeAsync(
            _ => Task.FromResult(ambient.Value));
        ambient.Value = "changed";

        releaseBlocker.SetResult();
        await blocker.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("captured", await captured.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task FailedCommand_DoesNotStopFollowingCommands()
    {
        await using var dispatcher = new ThreadCommandDispatcher("thread_failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.InvokeAsync(
                _ => throw new InvalidOperationException("expected")));

        var result = await dispatcher.InvokeAsync(
            _ => Task.FromResult(42));
        Assert.Equal(42, result);
    }
}
