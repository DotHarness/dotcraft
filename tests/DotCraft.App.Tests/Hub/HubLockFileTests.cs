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

        var currentBinaryPath = CurrentProcessBinaryPath();
        var info = new HubLockInfo(
            Pid: Environment.ProcessId,
            ApiBaseUrl: "http://127.0.0.1:43000",
            Token: "token",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            BinaryPath: currentBinaryPath);
        first!.Publish(info);

        Assert.False(HubLockFile.TryAcquire(paths, out var second, out var existingInfo));
        Assert.Null(second);
        Assert.NotNull(existingInfo);
        Assert.Equal(Environment.ProcessId, existingInfo!.Pid);
        Assert.Equal("http://127.0.0.1:43000", existingInfo.ApiBaseUrl);
        Assert.Equal(currentBinaryPath, existingInfo.BinaryPath);

        first.DeleteAfterDispose();
    }

    [Fact]
    public void TryAcquire_LivePidStartedAfterLockIsRecoverableStaleLock()
    {
        if (!CanReadCurrentProcessStartTime())
            return;

        var paths = HubPaths.Resolve(_userProfile);
        Directory.CreateDirectory(paths.HubStatePath);
        var guardPath = paths.LockFilePath + ".guard";
        var staleInfo = new HubLockInfo(
            Pid: Environment.ProcessId,
            ApiBaseUrl: "http://127.0.0.1:43003",
            Token: "token",
            StartedAt: DateTimeOffset.UnixEpoch,
            Version: "old",
            BinaryPath: CurrentProcessBinaryPath());
        WriteLock(paths.LockFilePath, staleInfo);
        File.WriteAllText(guardPath, "stale");

        Assert.True(HubLockFile.TryAcquire(paths, out var recovered, out var existingInfo));
        Assert.NotNull(recovered);
        Assert.NotNull(existingInfo);
        Assert.Equal(HubLockOwnerStatus.PidReused, existingInfo!.GetOwnerProcessStatus());
        Assert.Equal(staleInfo, HubLockFile.TryRead(paths.LockFilePath));
        Assert.True(File.Exists(guardPath));
        Assert.Equal(0, new FileInfo(guardPath).Length);

        recovered!.Publish(staleInfo with
        {
            Token = "replacement",
            StartedAt = DateTimeOffset.UtcNow
        });
        Assert.Equal("replacement", HubLockFile.TryRead(paths.LockFilePath)?.Token);

        recovered.DeleteAfterDispose();
    }

    [Fact]
    public void GetOwnerProcessStatus_DllLaunchedHubShapeRemainsAliveWhenPidWasNotReused()
    {
        if (!CanReadCurrentProcessStartTime())
            return;

        var info = new HubLockInfo(
            Pid: Environment.ProcessId,
            ApiBaseUrl: "http://127.0.0.1:43004",
            Token: "token",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            BinaryPath: typeof(HubLockInfo).Assembly.Location);

        Assert.EndsWith(".dll", info.BinaryPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HubLockOwnerStatus.Alive, info.GetOwnerProcessStatus());
        Assert.True(info.IsOwnerProcessAlive());
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

    private static void WriteLock(string lockFilePath, HubLockInfo info)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(info, HubJson.Options);
        File.WriteAllText(lockFilePath, json);
    }

    private static bool CanReadCurrentProcessStartTime()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            _ = process.StartTime;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string CurrentProcessBinaryPath()
    {
        try
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var fileName = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(fileName))
                return fileName;
        }
        catch
        {
            // Fall back below.
        }

        return Environment.ProcessPath ?? typeof(HubLockFileTests).Assembly.Location;
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
