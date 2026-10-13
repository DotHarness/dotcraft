namespace DotCraft.Dreams;

/// <summary>
/// A store's <c>.consolidate-lock</c>. Its modification time records the store's last consolidation,
/// and a consolidation holds the file open exclusively until it settles or its process exits.
/// </summary>
public sealed class ConsolidationLock
{
    public const string FileName = ".consolidate-lock";

    private readonly FileStream _handle;
    private readonly string _path;
    private readonly DateTime _acquiredAtUtc;

    private ConsolidationLock(FileStream handle, string path, DateTimeOffset? previousConsolidatedAt, DateTime acquiredAtUtc)
    {
        _handle = handle;
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

    /// <summary>Acquires the lock, recording <paramref name="now"/> as the last consolidation, or returns null while another holder has it open.</summary>
    public static ConsolidationLock? TryAcquire(string storeDirectory, DateTimeOffset now)
    {
        Directory.CreateDirectory(storeDirectory);
        var path = Path.Combine(storeDirectory, FileName);
        var previous = LastConsolidatedAt(storeDirectory);
        FileStream handle;
        try
        {
            handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }

        try
        {
            File.SetLastWriteTimeUtc(handle.SafeFileHandle, now.UtcDateTime);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        return new ConsolidationLock(handle, path, previous, now.UtcDateTime);
    }

    /// <summary>Releases the lock after a successful consolidation, keeping the acquire time as the last consolidation.</summary>
    public void Commit() => Release(_acquiredAtUtc);

    /// <summary>Releases the lock after a failed or canceled consolidation, restoring the previous last consolidation.</summary>
    public void Rollback() => Release(PreviousConsolidatedAt?.UtcDateTime);

    private void Release(DateTime? lastConsolidatedAtUtc)
    {
        using (_handle)
        {
            if (lastConsolidatedAtUtc is { } time)
                File.SetLastWriteTimeUtc(_handle.SafeFileHandle, time);
        }

        if (lastConsolidatedAtUtc is null)
            File.Delete(_path);
    }
}
