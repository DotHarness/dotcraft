namespace DotCraft.Dreams;

/// <summary>
/// A store's <c>.consolidate-lock</c>. While nonempty, its modification time records the store's last consolidation.
/// A consolidation holds the file open exclusively until it settles or its process exits, and the file is never
/// deleted, so every holder locks the same file.
/// </summary>
public sealed class ConsolidationLock
{
    public const string FileName = ".consolidate-lock";

    private static readonly byte[] ConsolidatedMarker = "1"u8.ToArray();

    private readonly FileStream _handle;
    private readonly DateTime _acquiredAtUtc;

    private ConsolidationLock(FileStream handle, DateTimeOffset? previousConsolidatedAt, DateTime acquiredAtUtc)
    {
        _handle = handle;
        PreviousConsolidatedAt = previousConsolidatedAt;
        _acquiredAtUtc = acquiredAtUtc;
    }

    public DateTimeOffset? PreviousConsolidatedAt { get; }

    public static DateTimeOffset? LastConsolidatedAt(string storeDirectory)
    {
        var file = new FileInfo(Path.Combine(storeDirectory, FileName));
        return file.Exists && file.Length > 0 ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) : null;
    }

    /// <summary>Acquires the lock, recording <paramref name="now"/> as the last consolidation, or returns null while another holder has it open.</summary>
    public static ConsolidationLock? TryAcquire(string storeDirectory, DateTimeOffset now)
    {
        Directory.CreateDirectory(storeDirectory);
        FileStream handle;
        try
        {
            handle = new FileStream(Path.Combine(storeDirectory, FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }

        try
        {
            DateTimeOffset? previous = handle.Length > 0
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(handle.SafeFileHandle), TimeSpan.Zero)
                : null;
            Mark(handle, now.UtcDateTime);
            return new ConsolidationLock(handle, previous, now.UtcDateTime);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Releases the lock after a successful consolidation, keeping the acquire time as the last consolidation.</summary>
    public void Commit()
    {
        using (_handle)
            Mark(_handle, _acquiredAtUtc);
    }

    /// <summary>Releases the lock after a failed or canceled consolidation, restoring the previous last consolidation.</summary>
    public void Rollback()
    {
        using (_handle)
        {
            if (PreviousConsolidatedAt is { } previous)
                Mark(_handle, previous.UtcDateTime);
            else
                _handle.SetLength(0);
        }
    }

    private static void Mark(FileStream handle, DateTime lastConsolidatedAtUtc)
    {
        handle.SetLength(0);
        handle.Position = 0;
        handle.Write(ConsolidatedMarker);
        handle.Flush(flushToDisk: true);
        File.SetLastWriteTimeUtc(handle.SafeFileHandle, lastConsolidatedAtUtc);
    }
}
