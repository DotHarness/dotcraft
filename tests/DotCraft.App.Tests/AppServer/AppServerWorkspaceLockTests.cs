using DotCraft.AppServer;
using DotCraft.Hosting;

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
    public void CleanupStaleFiles_RemovesStaleLockAndGuard()
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(BotPath);
        WriteLock(lockPath, pid: 999999);
        File.WriteAllText(lockPath + ".guard", string.Empty);

        AppServerWorkspaceLock.CleanupStaleFiles(BotPath);

        Assert.False(File.Exists(lockPath));
        Assert.False(File.Exists(lockPath + ".guard"));
    }

    [Fact]
    public void CleanupStaleFiles_PreservesLiveLockAndGuard()
    {
        var lockPath = AppServerWorkspaceLock.GetLockFilePath(BotPath);
        WriteLock(lockPath, pid: Environment.ProcessId);
        File.WriteAllText(lockPath + ".guard", string.Empty);

        AppServerWorkspaceLock.CleanupStaleFiles(BotPath);

        Assert.True(File.Exists(lockPath));
        Assert.True(File.Exists(lockPath + ".guard"));
    }

    private void WriteLock(string lockPath, int pid)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new AppServerLockInfo(
            Pid: pid,
            WorkspacePath: _workspacePath,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string>()), DotCraft.Hub.HubJson.Options);
        File.WriteAllText(lockPath, json);
    }

    private DotCraftPaths Paths() => new()
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
