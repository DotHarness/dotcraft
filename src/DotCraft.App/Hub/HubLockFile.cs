using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Processes;

namespace DotCraft.Hub;

/// <summary>
/// Holds the Hub single-instance lock and publishes discovery metadata.
/// </summary>
public sealed class HubLockFile : IDisposable
{
    private readonly CrossProcessFileLock _fileLock;
    private bool _disposed;

    private HubLockFile(CrossProcessFileLock fileLock) => _fileLock = fileLock;

    /// <summary>
    /// Attempts to acquire the Hub lock file.
    /// </summary>
    public static bool TryAcquire(HubPaths paths, out HubLockFile? lockFile, out HubLockInfo? existingInfo)
    {
        Directory.CreateDirectory(paths.HubStatePath);
        existingInfo = TryRead(paths.LockFilePath);
        if (!CrossProcessFileLock.TryAcquire(paths.LockFilePath, out var fileLock))
        {
            existingInfo ??= TryRead(paths.LockFilePath);
            lockFile = null;
            return false;
        }

        lockFile = new HubLockFile(fileLock!);
        return true;
    }

    /// <summary>
    /// Writes the current Hub discovery metadata to the lock file.
    /// </summary>
    public void Publish(HubLockInfo info)
    {
        var json = JsonSerializer.Serialize(info, HubJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        _fileLock.Write(bytes);
    }

    /// <summary>
    /// Attempts to delete the lock file after the lock stream is closed.
    /// </summary>
    public void DeleteAfterDispose()
    {
        _disposed = true;
        _fileLock.DeleteAfterDispose();
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

    internal bool IsOwnerProcessAlive() => GetOwnerProcessStatus() == HubLockOwnerStatus.Alive;

    internal HubLockOwnerStatus GetOwnerProcessStatus()
    {
        if (Pid <= 0)
            return HubLockOwnerStatus.NotRunning;

        try
        {
            using var process = Process.GetProcessById(Pid);
            if (process.HasExited)
                return HubLockOwnerStatus.NotRunning;

            if (StartedAt != default && TryCheckProcessStartedAfterLock(process, out var pidReused))
                return pidReused ? HubLockOwnerStatus.PidReused : HubLockOwnerStatus.Alive;

            return MatchesRecordedBinary(process)
                ? HubLockOwnerStatus.Alive
                : HubLockOwnerStatus.PidReused;
        }
        catch
        {
            return HubLockOwnerStatus.NotRunning;
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
            {
                if (PathsEqual(actualPath, BinaryPath))
                    return true;

                if (IsDllPath(BinaryPath) && IsDotNetHostPath(actualPath))
                    return true;

                return false;
            }
        }
        catch
        {
            // Some platforms/processes deny executable path inspection. Fall back to process-name matching.
        }

        if (IsDllPath(BinaryPath) && IsDotNetHostName(process.ProcessName))
            return true;

        var expectedName = Path.GetFileNameWithoutExtension(BinaryPath);
        return !string.IsNullOrWhiteSpace(expectedName) &&
               string.Equals(process.ProcessName, expectedName, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCheckProcessStartedAfterLock(Process process, out bool startedAfterLock)
    {
        startedAfterLock = false;
        try
        {
            var processStartedAt = new DateTimeOffset(process.StartTime.ToUniversalTime());
            startedAfterLock = StartedAt.AddSeconds(1) < processStartedAt;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch
        {
            return string.Equals(left, right, comparison);
        }
    }

    private static bool IsDllPath(string path)
        => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsDotNetHostPath(string path)
        => IsDotNetHostName(Path.GetFileNameWithoutExtension(path));

    private static bool IsDotNetHostName(string? name)
        => string.Equals(name, "dotnet", StringComparison.OrdinalIgnoreCase);
}

internal enum HubLockOwnerStatus
{
    Alive,
    NotRunning,
    PidReused
}
