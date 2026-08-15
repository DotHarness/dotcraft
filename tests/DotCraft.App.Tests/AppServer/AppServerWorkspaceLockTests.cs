using DotCraft.Workspaces;
using DotCraft.AppServer;
using DotCraft.Hosting;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class AppServerWorkspaceLockTests : IDisposable
{
    private readonly string _workspacePath = Path.Combine(
        Path.GetTempPath(),
        "DotCraftAppServerLock_" + Guid.NewGuid().ToString("N"));

    private string BotPath => Path.Combine(_workspacePath, ".craft");

    public AppServerWorkspaceLockTests()
    {
        Directory.CreateDirectory(BotPath);
    }

    [Fact]
    public void TryAcquire_FirstAcquireSucceedsAndSecondDetectsExistingLock()
    {
        var paths = Paths();

        Assert.True(AppServerWorkspaceLock.TryAcquire(paths, out var first, out var initialInfo));
        Assert.Null(initialInfo);
        Assert.NotNull(first);

        first!.Publish(new AppServerLockInfo(
            Pid: Environment.ProcessId,
            WorkspacePath: _workspacePath,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string>
            {
                ["appServerWebSocket"] = "ws://127.0.0.1:43001/ws?token=t"
            }));

        Assert.False(AppServerWorkspaceLock.TryAcquire(paths, out var second, out var existingInfo));
        Assert.Null(second);
        Assert.NotNull(existingInfo);
        Assert.Equal(Environment.ProcessId, existingInfo!.Pid);
        Assert.True(existingInfo.ManagedByHub);

        first.DeleteAfterDispose();
    }

    [Fact]
    public void TryAcquire_ActiveLockWithoutMetadataBlocksSecondOwner()
    {
        var paths = Paths();

        Assert.True(AppServerWorkspaceLock.TryAcquire(paths, out var first, out _));
        Assert.False(AppServerWorkspaceLock.TryAcquire(paths, out var second, out var existingInfo));
        Assert.Null(second);
        Assert.Null(existingInfo);

        first!.DeleteAfterDispose();
    }

    [Fact]
    public void TryAcquire_AfterReleaseTreatsOldLockAsRecoverable()
    {
        var paths = Paths();

        Assert.True(AppServerWorkspaceLock.TryAcquire(paths, out var first, out _));
        first!.Publish(new AppServerLockInfo(
            Pid: 999999,
            WorkspacePath: _workspacePath,
            ManagedByHub: false,
            HubApiBaseUrl: null,
            StartedAt: DateTimeOffset.UtcNow,
            Version: "old",
            Endpoints: new Dictionary<string, string>()));
        first.Dispose();

        Assert.True(AppServerWorkspaceLock.TryAcquire(paths, out var second, out var existingInfo));
        Assert.NotNull(second);
        Assert.NotNull(existingInfo);
        Assert.Equal(999999, existingInfo!.Pid);

        second!.DeleteAfterDispose();
    }

    [Fact]
    public void TryAcquire_RecoversUnheldLockWithoutMetadata()
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(BotPath);
        File.WriteAllText(lockPath, string.Empty);

        Assert.True(AppServerWorkspaceLock.TryAcquire(Paths(), out var recovered, out var existingInfo));
        Assert.NotNull(recovered);
        Assert.Null(existingInfo);

        recovered!.DeleteAfterDispose();
    }

    [Fact]
    public async Task TryAcquire_ConcurrentCallersHaveSingleWinner()
    {
        var paths = Paths();
        using var ready = new Barrier(2);

        async Task<AppServerWorkspaceLock?> CompeteAsync()
        {
            return await Task.Run(() =>
            {
                ready.SignalAndWait();
                AppServerWorkspaceLock.TryAcquire(paths, out var acquired, out _);
                return acquired;
            });
        }

        var attempts = await Task.WhenAll(CompeteAsync(), CompeteAsync());
        var winner = Assert.Single(attempts, candidate => candidate is not null);
        winner!.DeleteAfterDispose();
    }

    private WorkspacePaths Paths() => new()
    {
        WorkspacePath = _workspacePath,
        CraftPath = BotPath
    };

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspacePath))
                Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
