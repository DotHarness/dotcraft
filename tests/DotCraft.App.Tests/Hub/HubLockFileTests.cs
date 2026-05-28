using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class HubLockFileTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubLock_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryAcquire_FirstAcquireSucceedsAndSecondDetectsExistingLock()
    {
        var paths = HubPaths.Resolve(_userProfile);

        Assert.True(HubLockFile.TryAcquire(paths, out var first, out var initialInfo));
        Assert.Null(initialInfo);
        Assert.NotNull(first);

        var info = new HubLockInfo(
            Pid: Environment.ProcessId,
            ApiBaseUrl: "http://127.0.0.1:43000",
            Token: "token",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            BinaryPath: "C:\\tools\\dotcraft.exe");
        first!.Publish(info);

        Assert.False(HubLockFile.TryAcquire(paths, out var second, out var existingInfo));
        Assert.Null(second);
        Assert.NotNull(existingInfo);
        Assert.Equal(Environment.ProcessId, existingInfo!.Pid);
        Assert.Equal("http://127.0.0.1:43000", existingInfo.ApiBaseUrl);
        Assert.Equal("C:\\tools\\dotcraft.exe", existingInfo.BinaryPath);

        first.DeleteAfterDispose();
    }

    [Fact]
    public void TryRead_OldLockWithoutBinaryPathRemainsCompatible()
    {
        var paths = HubPaths.Resolve(_userProfile);
        Directory.CreateDirectory(paths.HubStatePath);
        File.WriteAllText(paths.LockFilePath, """
        {
          "pid": 999999,
          "apiBaseUrl": "http://127.0.0.1:43002",
          "token": "token",
          "startedAt": "2026-05-16T00:00:00Z",
          "version": "old"
        }
        """);

        var info = HubLockFile.TryRead(paths.LockFilePath);

        Assert.NotNull(info);
        Assert.Equal(999999, info!.Pid);
        Assert.Null(info.BinaryPath);
    }

    [Fact]
    public void TryAcquire_AfterReleaseTreatsOldLockAsRecoverable()
    {
        var paths = HubPaths.Resolve(_userProfile);

        Assert.True(HubLockFile.TryAcquire(paths, out var first, out _));
        first!.Publish(new HubLockInfo(
            Pid: 999999,
            ApiBaseUrl: "http://127.0.0.1:43001",
            Token: "old",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "old"));
        first.Dispose();

        Assert.True(HubLockFile.TryAcquire(paths, out var second, out var existingInfo));
        Assert.NotNull(second);
        Assert.NotNull(existingInfo);
        Assert.Equal(999999, existingInfo!.Pid);

        second!.DeleteAfterDispose();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_userProfile))
                Directory.Delete(_userProfile, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
