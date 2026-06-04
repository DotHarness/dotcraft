using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotCraft.Hub;

/// <summary>
/// Holds the Hub single-instance lock and publishes discovery metadata.
/// </summary>
public sealed class HubLockFile : IDisposable
{
    private readonly string _lockFilePath;
    private readonly CrossProcessFileLock _fileLock;
    private bool _disposed;

    private HubLockFile(string lockFilePath, CrossProcessFileLock fileLock)
    {
        _lockFilePath = lockFilePath;
        _fileLock = fileLock;
    }

    /// <summary>
    /// Attempts to acquire the Hub lock file.
    /// </summary>
    public static bool TryAcquire(HubPaths paths, out HubLockFile? lockFile, out HubLockInfo? existingInfo)
    {
        Directory.CreateDirectory(paths.HubStatePath);
        existingInfo = TryRead(paths.LockFilePath);
        if (existingInfo is not null && existingInfo.IsLiveHubProcess())
        {
            lockFile = null;
            return false;
        }

        if (existingInfo is not null)
            DeleteGuardFile(paths.LockFilePath);

        if (!CrossProcessFileLock.TryAcquire(paths.LockFilePath, out var fileLock))
        {
            existingInfo ??= TryRead(paths.LockFilePath);
            lockFile = null;
            return false;
        }

        lockFile = new HubLockFile(paths.LockFilePath, fileLock!);
        return true;
    }

    /// <summary>
    /// Writes the current Hub discovery metadata to the lock file.
    /// </summary>
    public void Publish(HubLockInfo info)
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
    /// Attempts to delete the lock file after the lock stream is closed.
    /// </summary>
    public void DeleteAfterDispose()
    {
        Dispose();
        try
        {
            File.Delete(_lockFilePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }

        try
        {
            File.Delete(_fileLock.GuardPath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _fileLock.Dispose();
    }

    /// <summary>
    /// Reads Hub discovery metadata from disk.
    /// </summary>
    public static HubLockInfo? TryRead(string lockFilePath)
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

            return JsonSerializer.Deserialize<HubLockInfo>(json, HubJson.Options);
        }
        catch
        {
            return null;
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
/// Published Hub discovery metadata.
/// </summary>
public sealed record HubLockInfo(
    int Pid,
    string ApiBaseUrl,
    string Token,
    DateTimeOffset StartedAt,
    string Version,
    string? BinaryPath = null)
{
    /// <summary>
    /// Returns whether the recorded process appears to still exist.
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

    /// <summary>
    /// Returns whether the recorded PID still appears to belong to the Hub process described by this lock.
    /// </summary>
    public bool IsLiveHubProcess()
    {
        try
        {
            using var process = Process.GetProcessById(Pid);
            if (process.HasExited)
                return false;

            return MatchesRecordedBinary(process);
        }
        catch
        {
            return false;
        }
    }

    private bool MatchesRecordedBinary(Process process)
    {
        if (string.IsNullOrWhiteSpace(BinaryPath))
            return true;

        try
        {
            var actualPath = process.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(actualPath))
                return PathsEqual(actualPath, BinaryPath);
        }
        catch
        {
            // Some platforms/processes deny executable path inspection. Fall back to process-name matching
            // so a clearly unrelated PID reuse does not keep a stale hub.lock alive indefinitely.
        }

        var expectedName = Path.GetFileNameWithoutExtension(BinaryPath);
        return !string.IsNullOrWhiteSpace(expectedName) &&
               string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }
}
