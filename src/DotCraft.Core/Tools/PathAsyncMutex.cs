namespace DotCraft.Tools;

/// <summary>
/// Process-wide async mutex keyed by canonical file path.
/// </summary>
internal static class PathAsyncMutex
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    internal static async Task<IDisposable> AcquireAsync(string fullPath, CancellationToken cancellationToken = default)
        => await AcquireKeyAsync(Canonicalize(fullPath), cancellationToken);

    internal static async Task<IDisposable> AcquireKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        Entry entry;
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                Entries[key] = entry;
            }

            entry.RefCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }

        return new Releaser(key, entry);
    }

    private static string Canonicalize(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        return OperatingSystem.IsWindows()
            ? path.ToLowerInvariant()
            : path;
    }

    private static void ReleaseReference(string key, Entry entry)
    {
        lock (Gate)
        {
            entry.RefCount--;
            if (entry.RefCount == 0)
            {
                Entries.Remove(key);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        internal readonly SemaphoreSlim Semaphore = new(1, 1);
        internal int RefCount;
    }

    private sealed class Releaser(string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            entry.Semaphore.Release();
            ReleaseReference(key, entry);
        }
    }
}
