using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Memory;
using DotCraft.Tools;

namespace DotCraft.Dreams;

/// <summary>A store's files as Dreams snapshots, compares, and replaces them: its top-level Markdown files, keyed by name.</summary>
internal static partial class DreamsStoreFiles
{
    public const int MaxMemoryFileBytes = 100_000;

    public static bool IsMemoryFileName(string name) =>
        name.Length <= 120 && SafeName().IsMatch(name) && !name.Contains("..", StringComparison.Ordinal);

    public static bool IsIndex(string name) =>
        string.Equals(name, MemoryStore.IndexFileName, StringComparison.OrdinalIgnoreCase);

    public static bool HasMemoryFiles(string directory) =>
        Directory.Exists(directory) && MarkdownFiles(directory).Any(path => !IsIndex(Path.GetFileName(path)));

    public static Dictionary<string, byte[]> Read(string directory)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
            return files;

        foreach (var path in MarkdownFiles(directory))
            files[Path.GetFileName(path)] = File.ReadAllBytes(path);
        return files;
    }

    private static IEnumerable<string> MarkdownFiles(string directory) =>
        Directory.EnumerateFiles(directory)
            .Where(static path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase));

    public static void Write(string directory, IReadOnlyDictionary<string, byte[]> files)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in files)
            File.WriteAllBytes(Path.Combine(directory, name), content);
    }

    public static DreamsRunChanges Diff(IReadOnlyDictionary<string, byte[]> baseline, IReadOnlyDictionary<string, byte[]> candidate)
    {
        var changes = new DreamsRunChanges();
        foreach (var (name, content) in candidate.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!baseline.TryGetValue(name, out var before))
                Record(changes, name, changes.Added);
            else if (!before.AsSpan().SequenceEqual(content))
                Record(changes, name, changes.Updated);
        }

        foreach (var name in baseline.Keys.Where(name => !candidate.ContainsKey(name)).Order(StringComparer.OrdinalIgnoreCase))
            Record(changes, name, changes.Deleted);
        return changes;
    }

    public static IReadOnlyList<DreamsFileChange> Review(IReadOnlyDictionary<string, byte[]> baseline, IReadOnlyDictionary<string, byte[]> candidate)
    {
        var files = new List<DreamsFileChange>();
        foreach (var name in baseline.Keys.Union(candidate.Keys, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(IsIndex)
                     .ThenBy(static name => name, StringComparer.OrdinalIgnoreCase))
        {
            var before = baseline.GetValueOrDefault(name);
            var after = candidate.GetValueOrDefault(name);
            if (before != null && after != null && before.AsSpan().SequenceEqual(after))
                continue;

            var change = before == null ? "added" : after == null ? "deleted" : "updated";
            files.Add(new DreamsFileChange(name, change, Decode(before), Decode(after)));
        }

        return files;
    }

    /// <summary>Returns why the candidate store is invalid, or null when it may replace a store.</summary>
    public static string? Validate(string candidateDirectory)
    {
        if (Directory.EnumerateDirectories(candidateDirectory).FirstOrDefault() is { } directory)
            return $"The candidate store holds a subdirectory: {Path.GetFileName(directory)}";

        foreach (var path in Directory.EnumerateFiles(candidateDirectory))
        {
            var name = Path.GetFileName(path);
            if (!string.Equals(Path.GetExtension(name), ".md", StringComparison.OrdinalIgnoreCase))
                return $"The candidate store holds a file that is not Markdown: {name}";
            if (IsIndex(name))
                continue;
            if (!IsMemoryFileName(name))
                return $"The memory file name is not a safe slug: {name}";
            if (new FileInfo(path).Length > MaxMemoryFileBytes)
                return $"The memory file is larger than {MaxMemoryFileBytes:N0} bytes: {name}";
        }

        var indexPath = Path.Combine(candidateDirectory, MemoryStore.IndexFileName);
        if (!File.Exists(indexPath))
            return null;

        var index = File.ReadAllText(indexPath, Encoding.UTF8).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var lines = index.Length == 0 ? 0 : index.Count(static c => c == '\n') + 1;
        if (lines > MemoryIndex.MaxLines)
            return $"MEMORY.md has {lines} lines, over its {MemoryIndex.MaxLines}-line limit.";
        var bytes = Encoding.UTF8.GetByteCount(index);
        return bytes > MemoryIndex.MaxBytes
            ? $"MEMORY.md has {bytes:N0} bytes, over its {MemoryIndex.MaxBytes:N0}-byte limit."
            : null;
    }

    /// <summary>Replaces the store's Markdown files with <paramref name="replacement"/> only while they still equal <paramref name="expected"/>.</summary>
    public static bool TryReplace(
        string storeDirectory,
        IReadOnlyDictionary<string, byte[]> expected,
        IReadOnlyDictionary<string, byte[]> replacement)
    {
        Directory.CreateDirectory(storeDirectory);
        using var storeLock = PathAsyncMutex.AcquireDirectory(storeDirectory);
        using var indexLock = PathAsyncMutex.Acquire(Path.Combine(storeDirectory, MemoryStore.IndexFileName));
        var current = Read(storeDirectory);
        if (!SameFiles(current, expected))
            return false;

        foreach (var (name, content) in replacement)
        {
            if (!current.TryGetValue(name, out var existing) || !existing.AsSpan().SequenceEqual(content))
                WriteAtomic(Path.Combine(storeDirectory, name), content);
        }

        foreach (var name in current.Keys.Where(name => !replacement.ContainsKey(name)))
            File.Delete(Path.Combine(storeDirectory, name));
        return true;
    }

    public static bool SameFiles(IReadOnlyDictionary<string, byte[]> left, IReadOnlyDictionary<string, byte[]> right) =>
        left.Count == right.Count
        && left.All(pair => right.TryGetValue(pair.Key, out var other) && pair.Value.AsSpan().SequenceEqual(other));

    private static void Record(DreamsRunChanges changes, string name, List<string> list)
    {
        if (IsIndex(name))
            changes.IndexChanged = true;
        else
            list.Add(name);
    }

    private static string? Decode(byte[]? content) => content == null ? null : Encoding.UTF8.GetString(content);

    private static void WriteAtomic(string path, byte[] content)
    {
        var tempFile = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        File.WriteAllBytes(tempFile, content);
        try
        {
            File.Move(tempFile, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-][A-Za-z0-9._-]*\.[mM][dD]$")]
    private static partial Regex SafeName();
}
