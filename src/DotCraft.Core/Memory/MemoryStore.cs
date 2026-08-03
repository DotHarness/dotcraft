using System.Text;
using System.Collections.Concurrent;
using DotCraft.Sessions;

namespace DotCraft.Memory;

/// <summary>
/// Dual-layer memory: MEMORY.md (structured long-term facts, always in context) +
/// HISTORY.md (append-only grep-searchable event log, not in context).
/// </summary>
public sealed class MemoryStore
{
    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _memoryDir;
    
    private readonly string _longTermFile;

    private readonly string _historyFile;

    private readonly object _syncRoot;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
        _historyFile = Path.Combine(_memoryDir, "HISTORY.md");
        _syncRoot = StoreLocks.GetOrAdd(Path.GetFullPath(_memoryDir), static _ => new object());
    }

    /// <summary>
    /// Gets the path to the MEMORY.md file.
    /// </summary>
    public string LongTermFilePath => _longTermFile;

    /// <summary>
    /// Gets the path to the workspace memory directory.
    /// </summary>
    public string MemoryDirectoryPath => _memoryDir;

    /// <summary>
    /// Gets the path to the HISTORY.md file.
    /// </summary>
    public string HistoryFilePath => _historyFile;

    /// <summary>
    /// Read long-term memory (MEMORY.md).
    /// </summary>
    public string ReadLongTerm()
    {
        lock (_syncRoot)
        {
            return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>
    /// Write to long-term memory (MEMORY.md).
    /// </summary>
    public bool WriteLongTerm(string content)
    {
        lock (_syncRoot)
        {
            WriteLongTermAtomic(content);
            return true;
        }
    }

    /// <summary>
    /// Append a timestamped entry to HISTORY.md (grep-searchable event log).
    /// Each entry is a paragraph followed by a blank line.
    /// </summary>
    public bool AppendHistory(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        lock (_syncRoot)
        {
            AppendHistoryCore(entry);
            return true;
        }
    }

    /// <summary>
    /// Saves a consolidation result under the memory-store lock.
    /// </summary>
    public MemoryStoreConsolidationWriteResult SaveConsolidation(string? historyEntry, string? memoryUpdate)
    {
        lock (_syncRoot)
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
    }

    /// <summary>
    /// Read the full HISTORY.md content (used during consolidation).
    /// </summary>
    public string ReadHistory()
    {
        lock (_syncRoot)
        {
            return File.Exists(_historyFile) ? File.ReadAllText(_historyFile, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>
    /// Ensures HISTORY.md exists so maintenance agents can append to a known file.
    /// </summary>
    public void EnsureHistoryFile()
    {
        lock (_syncRoot)
        {
            RejectReparsePointRoot();
            Directory.CreateDirectory(_memoryDir);
            if (!File.Exists(_historyFile))
                File.WriteAllText(_historyFile, string.Empty, Encoding.UTF8);
        }
    }

    internal void RestoreHistoryForConsolidation(string content)
    {
        lock (_syncRoot)
        {
            RejectReparsePointRoot();
            Directory.CreateDirectory(_memoryDir);
            File.WriteAllText(_historyFile, content, Encoding.UTF8);
        }
    }

    internal void RestoreLongTermForConsolidation(string content, bool existed)
    {
        lock (_syncRoot)
        {
            RejectReparsePointRoot();
            Directory.CreateDirectory(_memoryDir);
            if (existed)
            {
                WriteLongTermAtomic(content);
            }
            else if (File.Exists(_longTermFile))
            {
                File.Delete(_longTermFile);
            }
        }
    }

    /// <summary>
    /// Clears every file and subdirectory in the memory directory while preserving the root directory.
    /// </summary>
    public void ClearAll()
    {
        lock (_syncRoot)
        {
            RejectReparsePointRoot();
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

    /// <summary>
    /// Get combined memory context for agent (long-term memory only; HISTORY.md is searched on demand via grep).
    /// </summary>
    public string GetMemoryContext()
    {
        var longTerm = ReadLongTerm();
        return !string.IsNullOrWhiteSpace(longTerm) ? "## Long-term Memory\n" + longTerm : string.Empty;
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

/// <summary>
/// Describes which memory files changed during a consolidation write.
/// </summary>
public readonly record struct MemoryStoreConsolidationWriteResult(bool MemoryWritten, bool HistoryWritten)
{
    /// <summary>
    /// True when either MEMORY.md or HISTORY.md was changed.
    /// </summary>
    public bool AnyWritten => MemoryWritten || HistoryWritten;
}
