using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Hosting;
using DotCraft.Hub;

namespace DotCraft.AppServer;

/// <summary>
/// Process-level lock that prevents multiple AppServer instances from owning one workspace.
/// </summary>
public sealed class AppServerWorkspaceLock : IDisposable
{
    private readonly string _lockFilePath;
    private readonly CrossProcessFileLock _fileLock;
    private bool _disposed;

    private AppServerWorkspaceLock(string lockFilePath, CrossProcessFileLock fileLock)
    {
        _lockFilePath = lockFilePath;
        _fileLock = fileLock;
    }

    /// <summary>
    /// Attempts to acquire the workspace AppServer lock.
    /// </summary>
    public static bool TryAcquire(DotCraftPaths paths, out AppServerWorkspaceLock? lockFile, out AppServerLockInfo? existingInfo)
    {
        Directory.CreateDirectory(paths.CraftPath);
        var lockPath = GetLockFilePath(paths.CraftPath);
        existingInfo = TryRead(lockPath);
        if (existingInfo is not null && existingInfo.IsProcessAlive())
        {
            lockFile = null;
            return false;
        }

        if (existingInfo is not null)
            DeleteGuardFile(lockPath);

        if (!CrossProcessFileLock.TryAcquire(lockPath, out var fileLock))
        {
            existingInfo ??= TryRead(lockPath);
            lockFile = null;
            return false;
        }

        lockFile = new AppServerWorkspaceLock(lockPath, fileLock!);
        return true;
    }

    /// <summary>
    /// Writes current AppServer owner metadata to the lock file.
    /// </summary>
    public void Publish(AppServerLockInfo info)
    {
        var json = JsonSerializer.Serialize(info, HubJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        using var stream = new FileStream(
            _lockFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.ReadWrite);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Attempts to read AppServer owner metadata.
    /// </summary>
    public static AppServerLockInfo? TryRead(string lockFilePath)
    {
        try
        {
            if (!File.Exists(lockFilePath))
                return null;

            using var stream = new FileStream(
                lockFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<AppServerLockInfo>(json, HubJson.Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the AppServer lock path for a workspace `.craft` directory.
    /// </summary>
    public static string GetLockFilePath(string craftPath) => Path.Combine(craftPath, "appserver.lock");

    /// <summary>
    /// Best-effort cleanup for stale AppServer lock metadata and guard files.
    /// </summary>
    internal static void CleanupStaleFiles(string craftPath)
    {
        var lockPath = GetLockFilePath(craftPath);
        var info = TryRead(lockPath);
        if (info is not null && info.IsProcessAlive())
            return;

        DeleteLockFile(lockPath);
        DeleteGuardFile(lockPath);
    }

    /// <summary>
    /// Attempts to delete the lock file after releasing the stream.
    /// </summary>
    public void DeleteAfterDispose()
    {
        Dispose();
        DeleteLockFile(_lockFilePath);
        DeleteGuardFile(_lockFilePath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _fileLock.Dispose();
    }

    private static void DeleteLockFile(string lockFilePath)
    {
        try
        {
            File.Delete(lockFilePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void DeleteGuardFile(string lockFilePath)
    {
        try
        {
            File.Delete(lockFilePath + ".guard");
        }
        catch
        {
            // Best-effort stale guard cleanup only.
        }
    }
}

/// <summary>
/// Published workspace AppServer owner metadata.
/// </summary>
public sealed record AppServerLockInfo(
    int Pid,
    string WorkspacePath,
    bool ManagedByHub,
    string? HubApiBaseUrl,
    DateTimeOffset StartedAt,
    string Version,
    IReadOnlyDictionary<string, string> Endpoints)
{
    /// <summary>
    /// Returns whether the recorded owner process appears alive.
    /// </summary>
    public bool IsProcessAlive()
    {
        try
        {
            return !Process.GetProcessById(Pid).HasExited;
        }
        catch
        {
            return false;
        }
    }
}
