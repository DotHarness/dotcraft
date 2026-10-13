using System.Text;
using DotCraft.Security;
using DotCraft.Tools;

namespace DotCraft.Memory;

public sealed class MemoryStore
{
    public const string IndexFileName = "MEMORY.md";

    public MemoryStore(string dataPath)
    {
        MemoryDirectoryPath = DirectoryOf(dataPath);
        Directory.CreateDirectory(MemoryDirectoryPath);
        IndexFilePath = Path.Combine(MemoryDirectoryPath, IndexFileName);
    }

    public string MemoryDirectoryPath { get; }

    public string IndexFilePath { get; }

    public static string DirectoryOf(string dataPath) => Path.Combine(dataPath, "memory");

    /// <summary>The whole index file, unbounded; prompts use <see cref="MemoryIndex"/> instead.</summary>
    public string ReadIndexText() => ReadIndexText(IndexFilePath);

    public void ClearAll()
    {
        using var indexLock = PathAsyncMutex.Acquire(IndexFilePath);
        RejectReparsePointRoot();
        Directory.CreateDirectory(MemoryDirectoryPath);

        foreach (var entry in Directory.EnumerateFileSystemEntries(MemoryDirectoryPath).ToArray())
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

    internal static string ReadIndexText(string indexFilePath)
    {
        using var indexLock = PathAsyncMutex.Acquire(indexFilePath);
        return File.Exists(indexFilePath) ? File.ReadAllText(indexFilePath, Encoding.UTF8) : string.Empty;
    }

    internal static bool IsIndexFile(string storeDirectory, string fullPath) =>
        WorkspaceBoundary.IsWithin(fullPath, Path.Combine(storeDirectory, IndexFileName));

    private void RejectReparsePointRoot()
    {
        if (!Directory.Exists(MemoryDirectoryPath))
            return;

        var attributes = File.GetAttributes(MemoryDirectoryPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing to clear reparse-point memory directory: {MemoryDirectoryPath}");
    }
}
