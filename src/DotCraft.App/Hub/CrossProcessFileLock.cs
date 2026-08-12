namespace DotCraft.Hub;

internal sealed class CrossProcessFileLock : IDisposable
{
    private readonly string _lockPath;
    private readonly FileStream _stream;
    private bool _disposed;

    private CrossProcessFileLock(string lockPath, FileStream stream)
    {
        _lockPath = lockPath;
        _stream = stream;
    }

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

    public void Write(ReadOnlySpan<byte> bytes)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _stream.Position = 0;
        _stream.SetLength(0);
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    public void DeleteAfterDispose()
    {
        Dispose();
        RemoveUnheldFile(_lockPath);
    }

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
