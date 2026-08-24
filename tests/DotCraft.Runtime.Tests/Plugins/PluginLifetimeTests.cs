using DotCraft.Runtime;
using Xunit;

namespace DotCraft.Tests.Runtime.Plugins;

public sealed class PluginLifetimeTests
{
    [Fact]
    public void SignalStopping_IsIdempotent()
    {
        using var lifetime = new PluginLifetime();
        var callbacks = 0;
        using var registration = lifetime.Stopping.Register(() => callbacks++);

        lifetime.SignalStopping();
        lifetime.SignalStopping();

        Assert.True(lifetime.Stopping.IsCancellationRequested);
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public async Task BackgroundWork_DoesNotInheritTheActivationExecutionContext()
    {
        var ambient = new AsyncLocal<string?> { Value = "request-state" };
        var observed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var lifetime = new PluginLifetime();
        lifetime.Run(_ =>
        {
            observed.TrySetResult(ambient.Value);
            return Task.CompletedTask;
        });
        lifetime.Seal();

        lifetime.StartWork(_ => { });

        Assert.Null(await observed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await lifetime.DrainAsync(CancellationToken.None);
    }
}
