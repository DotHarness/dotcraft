using System.Text;
using System.Collections.Concurrent;
using DotCraft.Tools;

namespace DotCraft.Memory;

public sealed class MemoryStore
{
    public const int MaxContextChars = 10000;

    private static readonly ConcurrentDictionary<string, object> StoreLocks = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private readonly string _memoryDir;

    private readonly string _longTermFile;

    private readonly object _syncRoot;

    public MemoryStore(string workspaceRoot)
    {
        _memoryDir = Path.Combine(workspaceRoot, "memory");
        Directory.CreateDirectory(_memoryDir);
        _longTermFile = Path.Combine(_memoryDir, "MEMORY.md");
        _syncRoot = StoreLocks.GetOrAdd(Path.GetFullPath(_memoryDir), static _ => new object());
    }

    public string LongTermFilePath => _longTermFile;

    public string MemoryDirectoryPath => _memoryDir;

    public string ReadLongTerm()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
            return File.Exists(_longTermFile) ? File.ReadAllText(_longTermFile, Encoding.UTF8) : string.Empty;
        }
    }

    public void ClearAll()
    {
        lock (_syncRoot)
        {
            using var memoryLock = PathAsyncMutex.Acquire(_longTermFile);
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

    private void RejectReparsePointRoot()
    {
        if (!Directory.Exists(_memoryDir))
            return;

        var attributes = File.GetAttributes(_memoryDir);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to clear reparse-point memory directory: {_memoryDir}");
    }
}
