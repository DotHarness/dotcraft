using System.Collections.Concurrent;

namespace DotCraft.Hub;

/// <summary>
/// Cross-platform file lock backed by atomic guard-file creation plus an in-process registry.
/// </summary>
internal sealed class CrossProcessFileLock : IDisposable
{
    private static readonly ConcurrentDictionary<string, byte> HeldLocks = new(StringComparer.Ordinal);

    private readonly string _key;
    private readonly FileStream _stream;
    private bool _disposed;

    private CrossProcessFileLock(string key, string guardPath, FileStream stream)
    {
        _key = key;
        GuardPath = guardPath;
        _stream = stream;
    }

    public string GuardPath { get; }

    public static bool TryAcquire(string metadataPath, out CrossProcessFileLock? fileLock)
    {
        fileLock = null;
        var guardPath = metadataPath + ".guard";
        var key = Canonicalize(guardPath);

        if (!HeldLocks.TryAdd(key, 0))
            return false;

        try
        {
            var directory = Path.GetDirectoryName(guardPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var stream = new FileStream(
                guardPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read);

            fileLock = new CrossProcessFileLock(key, guardPath, stream);
            return true;
        }
        catch
        {
            HeldLocks.TryRemove(key, out _);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            _stream.Dispose();
        }
        finally
        {
            try
            {
                File.Delete(GuardPath);
            }
            catch
            {
                // Best-effort cleanup only.
            }

            HeldLocks.TryRemove(_key, out _);
        }
    }

    private static string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return OperatingSystem.IsWindows()
            ? fullPath.ToLowerInvariant()
            : fullPath;
    }
}
