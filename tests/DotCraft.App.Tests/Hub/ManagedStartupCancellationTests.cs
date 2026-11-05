using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class ManagedStartupCancellationTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "hub-cancel-" + Guid.NewGuid().ToString("N"));

    public ManagedStartupCancellationTests() => Directory.CreateDirectory(Path.Combine(_workspace, ".craft"));

    [Fact]
    public async Task Shutdown_CancelsPendingWorkspaceStartAndRejectsLaterAdmission()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        await using var registry = new ManagedAppServerRegistry(new HubEventBus(), "http://127.0.0.1:1", "token")
        {
            StartAppServerProcessAsync = async (_, _, _, ct) =>
            {
                Interlocked.Increment(ref starts);
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException();
            }
        };
        var request = new EnsureAppServerRequest { WorkspacePath = _workspace };
        var ensure = registry.EnsureAsync(request, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        registry.BeginShutdown();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ensure);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.EnsureAsync(request, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.RestartAsync(_workspace, null, CancellationToken.None));
        Assert.Equal(HubAppServerStates.Exited, registry.GetByWorkspace(_workspace).State);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task Shutdown_CancelsPendingProductServiceStartAndRejectsLaterAdmission()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paths = HubPaths.Resolve(_workspace);
        await using var registry = new ManagedLocalServiceRegistry(ManagedLocalServiceDefinitions.CreateBuiltIns(paths))
        {
            StartProcessAsync = async (_, ct) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException();
            }
        };
        var request = new EnsureManagedServiceRequest { ServiceId = "oratorio", Executable = typeof(HubHost).Assembly.Location };
        var ensure = registry.EnsureAsync(request, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        registry.BeginShutdown();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ensure);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => registry.EnsureAsync(request, CancellationToken.None));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace)) Directory.Delete(_workspace, true);
    }
}
