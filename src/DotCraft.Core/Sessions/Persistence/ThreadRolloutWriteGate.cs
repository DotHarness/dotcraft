namespace DotCraft.Sessions;

internal static class ThreadRolloutWriteGate
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

    public static async Task<IDisposable> AcquireAsync(
        string craftPath,
        string threadId,
        CancellationToken ct = default)
    {
        var key = CreateKey(craftPath, threadId);
        var entry = AddReference(key);
        try
        {
            await entry.Semaphore.WaitAsync(ct);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }

        return new Releaser(key, entry);
    }

    public static IDisposable Acquire(string craftPath, string threadId)
    {
        var key = CreateKey(craftPath, threadId);
        var entry = AddReference(key);
        try
        {
            entry.Semaphore.Wait();
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }

        return new Releaser(key, entry);
    }

    private static string CreateKey(string craftPath, string threadId)
    {
        var fullPath = Path.GetFullPath(craftPath);
        var normalizedPath = OperatingSystem.IsWindows()
            ? fullPath.ToLowerInvariant()
            : fullPath;
        return $"{normalizedPath}|{threadId}";
    }

    private static Entry AddReference(string key)
    {
        lock (Gate)
        {
            if (!Entries.TryGetValue(key, out var entry))
            {
                entry = new Entry();
                Entries[key] = entry;
            }

            entry.RefCount++;
            return entry;
        }
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
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int RefCount { get; set; }
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
