using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace DotCraft.Dreams;

/// <summary>
/// A store's <c>.consolidate-lock</c>. Its modification time records the store's last consolidation,
/// and while a consolidation runs its body holds the consolidating process id.
/// </summary>
public sealed class ConsolidationLock
{
    public const string FileName = ".consolidate-lock";

    /// <summary>A holder older than this is presumed dead even when its process id was reused.</summary>
    private static readonly TimeSpan HolderTimeout = TimeSpan.FromHours(1);

    private readonly string _path;
    private readonly DateTime _acquiredAtUtc;

    private ConsolidationLock(string path, DateTimeOffset? previousConsolidatedAt, DateTime acquiredAtUtc)
    {
        _path = path;
        PreviousConsolidatedAt = previousConsolidatedAt;
        _acquiredAtUtc = acquiredAtUtc;
    }

    public DateTimeOffset? PreviousConsolidatedAt { get; }

    public static DateTimeOffset? LastConsolidatedAt(string storeDirectory)
    {
        var path = Path.Combine(storeDirectory, FileName);
        return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
    }

    /// <summary>Acquires the lock, recording <paramref name="now"/> as the last consolidation, or returns null while a live process holds it.</summary>
    public static ConsolidationLock? TryAcquire(string storeDirectory, DateTimeOffset now)
    {
        Directory.CreateDirectory(storeDirectory);
        var path = Path.Combine(storeDirectory, FileName);
        var previous = LastConsolidatedAt(storeDirectory);
        if (previous is { } held && now - held < HolderTimeout && IsLive(ReadHolder(path)))
            return null;

        var self = Environment.ProcessId;
        File.WriteAllText(path, self.ToString(CultureInfo.InvariantCulture));
        if (ReadHolder(path) != self)
            return null;

        File.SetLastWriteTimeUtc(path, now.UtcDateTime);
        return new ConsolidationLock(path, previous, now.UtcDateTime);
    }

    /// <summary>Releases the lock after a successful consolidation, keeping the acquire time as the last consolidation.</summary>
    public void Commit()
    {
        File.WriteAllText(_path, string.Empty);
        File.SetLastWriteTimeUtc(_path, _acquiredAtUtc);
    }

    /// <summary>Releases the lock after a failed or canceled consolidation, restoring the previous last consolidation.</summary>
    public void Rollback()
    {
        if (PreviousConsolidatedAt is not { } previous)
        {
            File.Delete(_path);
            return;
        }

        File.WriteAllText(_path, string.Empty);
        File.SetLastWriteTimeUtc(_path, previous.UtcDateTime);
    }

    private static int? ReadHolder(string path)
    {
        try
        {
            return int.TryParse(File.ReadAllText(path).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                ? pid
                : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static bool IsLive(int? pid)
    {
        if (pid is not { } id)
            return false;

        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        {
            return true;
        }
    }
}
