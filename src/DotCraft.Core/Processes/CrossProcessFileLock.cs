namespace DotCraft.Processes;

/// <summary>
/// Exclusive lock file whose ownership is held by the operating system for the lifetime of the
/// owning process, so an abandoned file left by a crashed process is reclaimed instead of blocking.
/// </summary>
public sealed class CrossProcessFileLock : IDisposable
{
    private readonly string _lockPath;
    private readonly FileStream _stream;
    private bool _disposed;

    private CrossProcessFileLock(string lockPath, FileStream stream)
    {
        _lockPath = lockPath;
        _stream = stream;
    }

    /// <summary>Attempts to take the lock, reclaiming a stale file that no process still holds.</summary>
    public static bool TryAcquire(string lockPath, out CrossProcessFileLock? fileLock)
    {
        fileLock = null;
        var directory = Path.GetDirectoryName(lockPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.Read);
                fileLock = new CrossProcessFileLock(lockPath, stream);
                return true;
            }
            catch (IOException) when (File.Exists(lockPath))
            {
                if (attempt != 0 || !RemoveUnheldFile(lockPath))
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Reports whether another live process currently holds the lock file.</summary>
    public static bool IsHeld(string lockPath)
    {
        if (!File.Exists(lockPath))
            return false;

        try
        {
            using var stream = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Replaces the lock file payload that other processes may read while it is held.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stream.Position = 0;
        _stream.SetLength(0);
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>Releases the lock and removes the file when no other process took it meanwhile.</summary>
    public void DeleteAfterDispose()
    {
        Dispose();
        RemoveUnheldFile(_lockPath);
    }

    /// <summary>Releases the lock, leaving the file for the next acquirer to reclaim.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream.Dispose();
    }

    private static bool RemoveUnheldFile(string path)
    {
        if (!File.Exists(path))
            return true;

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
