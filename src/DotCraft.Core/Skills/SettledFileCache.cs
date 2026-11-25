using System.Collections.Concurrent;

namespace DotCraft.Skills;

internal sealed class SettledFileCache<T>(Func<string, T> read)
{
    // A file written this close to the read may change again without a visible size or timestamp change.
    private static readonly TimeSpan SettledWriteAge = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, (long Length, DateTime WriteUtc, T Value)> _entries =
        new(StringComparer.Ordinal);

    public T Get(string path)
    {
        var startedUtc = DateTime.UtcNow;
        var info = new FileInfo(path);
        if (!info.Exists)
            return read(path);

        var length = info.Length;
        var writeUtc = info.LastWriteTimeUtc;
        if (_entries.TryGetValue(path, out var entry) && entry.Length == length && entry.WriteUtc == writeUtc)
            return entry.Value;

        var value = read(path);
        if (writeUtc < startedUtc - SettledWriteAge)
            _entries[path] = (length, writeUtc, value);
        return value;
    }

    public void Clear() => _entries.Clear();
}
