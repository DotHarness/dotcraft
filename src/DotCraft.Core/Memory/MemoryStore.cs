using System.Text;
using System.Collections.Concurrent;
using DotCraft.Tools;

namespace DotCraft.Memory;

public sealed class MemoryStore
{
    public const int MaxContextChars = 10000;

    private static readonly ConcurrentDictionary<string, StoreState> StoreLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _memoryDir;

    private readonly string _longTermFile;

    private readonly string _historyFile;

    private readonly StoreState _syncRoot;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
        _historyFile = Path.Combine(_memoryDir, "HISTORY.md");
        _syncRoot = StoreLocks.GetOrAdd(Path.GetFullPath(_memoryDir), static _ => new StoreState());
    }

    public string LongTermFilePath => _longTermFile;

    public string MemoryDirectoryPath => _memoryDir;

    public string HistoryFilePath => _historyFile;

    public string ReadLongTerm()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
        }
    }

    public bool WriteLongTerm(string content)
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            WriteLongTermAtomic(content);
            return true;
        }
    }

    public bool AppendHistory(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            AppendHistoryCore(entry);
            return true;
        }
    }

    public MemoryStoreConsolidationWriteResult SaveConsolidation(string? historyEntry, string? memoryUpdate)
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            return SaveConsolidationCore(historyEntry, memoryUpdate);
        }
    }

    private MemoryStoreConsolidationWriteResult SaveConsolidationCore(string? historyEntry, string? memoryUpdate)
    {
        var historyWritten = false;
        var memoryWritten = false;

        if (!string.IsNullOrWhiteSpace(historyEntry))
        {
            AppendHistoryCore(historyEntry);
            historyWritten = true;
        }

        if (!string.IsNullOrWhiteSpace(memoryUpdate))
        {
            var current = File.Exists(_longTermFile)
                ? File.ReadAllText(_longTermFile, Encoding.UTF8)
                : string.Empty;
            if (!string.Equals(memoryUpdate, current, StringComparison.Ordinal))
            {
                WriteLongTermAtomic(memoryUpdate);
                memoryWritten = true;
            }
        }

        return new MemoryStoreConsolidationWriteResult(memoryWritten, historyWritten);
    }

    public string ReadHistory()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            return File.Exists(_historyFile) ? File.ReadAllText(_historyFile, Encoding.UTF8) : string.Empty;
        }
    }

    public void EnsureHistoryFile()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            RejectReparsePointRoot();
            Directory.CreateDirectory(_memoryDir);
            if (!File.Exists(_historyFile))
                File.WriteAllText(_historyFile, string.Empty, Encoding.UTF8);
        }
    }

    internal MemoryStoreSnapshot CaptureSnapshot(bool ensureHistoryFile = false)
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            RejectReparsePointRoot();
            if (ensureHistoryFile && !File.Exists(_historyFile))
            {
                Directory.CreateDirectory(_memoryDir);
                File.WriteAllText(_historyFile, string.Empty, Encoding.UTF8);
            }
            return CaptureSnapshotCore();
        }
    }

    internal MemoryStoreCommitResult TrySaveConsolidation(
        MemoryStoreSnapshot expected, string? historyEntry, string? memoryUpdate)
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            RejectReparsePointRoot();
            var current = CaptureSnapshotCore();
            if (current.Generation != expected.Generation)
                return new(MemoryStoreCommitOutcome.Reset, default);
            if (current != expected)
                return new(MemoryStoreCommitOutcome.Conflict, default);
            return new(MemoryStoreCommitOutcome.Committed, SaveConsolidationCore(historyEntry, memoryUpdate));
        }
    }

    private MemoryStoreSnapshot CaptureSnapshotCore() => new(
        _syncRoot.Generation,
        File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : null,
        File.Exists(_historyFile) ? File.ReadAllText(_historyFile, Encoding.UTF8) : null);

    public void ClearAll()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            using var historyLock = PathAsyncMutex.Acquire(_historyFile);
            RejectReparsePointRoot();
            _syncRoot.Generation++;
            Directory.CreateDirectory(_memoryDir);

            foreach (var entry in Directory.EnumerateFileSystemEntries(_memoryDir).ToArray())
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, recursive: (attributes & FileAttributes.ReparsePoint) == 0);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
    }

    public string GetMemoryContext()
    {
        var longTerm = ReadLongTerm();
        if (string.IsNullOrWhiteSpace(longTerm))
            return string.Empty;
        if (longTerm.Length > MaxContextChars)
            longTerm = longTerm[..MaxContextChars]
                + "\n\n[Memory excerpt truncated. Read the actual file before editing or recalling further details.]";
        return "## Long-term Memory\n" + longTerm;
    }

    private sealed class StoreState
    {
        internal long Generation;
    }

    private void AppendHistoryCore(string entry)
    {
        using var writer = new StreamWriter(_historyFile, append: true, Encoding.UTF8);
        writer.Write(entry.TrimEnd());
        writer.Write("\n\n");
    }

    private void WriteLongTermAtomic(string content)
    {
        Directory.CreateDirectory(_memoryDir);
        var tempFile = Path.Combine(_memoryDir, $".MEMORY.{Guid.NewGuid():N}.tmp");
        File.WriteAllText(tempFile, content, Encoding.UTF8);
        try
        {
            if (File.Exists(_longTermFile))
            {
                File.Replace(tempFile, _longTermFile, null);
            }
            else
            {
                File.Move(tempFile, _longTermFile);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private void RejectReparsePointRoot()
    {
        if (!Directory.Exists(_memoryDir))
            return;

        var attributes = File.GetAttributes(_memoryDir);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to clear reparse-point memory directory: {_memoryDir}");
    }
}

public readonly record struct MemoryStoreConsolidationWriteResult(bool MemoryWritten, bool HistoryWritten)
{
    public bool AnyWritten => MemoryWritten || HistoryWritten;
}

internal sealed record MemoryStoreSnapshot(long Generation, string? Memory, string? History);

internal enum MemoryStoreCommitOutcome
{
    Committed,
    Conflict,
    Reset
}

internal readonly record struct MemoryStoreCommitResult(
    MemoryStoreCommitOutcome Outcome, MemoryStoreConsolidationWriteResult Write);
