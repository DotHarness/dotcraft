using DotCraft.Tools.Sandbox;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class SandboxShellToolsCancellationTests
{
    [Fact]
    public async Task Exec_CallerCancellation_InterruptsExecutionAndPropagates()
    {
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? interruptedExecutionId = null;
        CancellationToken interruptToken = default;
        var client = new StubSandboxCommandClient
        {
            RunHandler = async (_, _, handlers, cancellationToken) =>
            {
                await handlers.OnInitialized!("execution_123");
                initialized.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable");
            },
            InterruptHandler = (executionId, cancellationToken) =>
            {
                interruptedExecutionId = executionId;
                interruptToken = cancellationToken;
                return Task.CompletedTask;
            }
        };
        var tools = new SandboxShellTools(client);
        using var cts = new CancellationTokenSource();

        var execution = tools.Exec("sleep 30", cancellationToken: cts.Token);
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal("execution_123", interruptedExecutionId);
        Assert.False(interruptToken.CanBeCanceled);
    }
}
