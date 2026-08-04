using DotCraft.Memory;
using Xunit;

namespace DotCraft.Tests.Memory;

public sealed class MemoryStoreTests : IDisposable
{
    private readonly string _tempDir;

    public MemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MemoryStore_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void WriteLongTerm_ReplacesContentAndLeavesNoTempFiles()
    {
        var store = new MemoryStore(_tempDir);

        Assert.True(store.WriteLongTerm("first"));
        Assert.True(store.WriteLongTerm("second"));

        Assert.Equal("second", store.ReadLongTerm());
        Assert.Empty(Directory.GetFiles(Path.Combine(_tempDir, "memory"), ".MEMORY.*.tmp"));
    }

    [Fact]
    public async Task SaveConsolidation_SerializesConcurrentHistoryAppends()
    {
        var store = new MemoryStore(_tempDir);
        var tasks = Enumerable.Range(0, 40)
            .Select(i => Task.Run(() => store.SaveConsolidation($"entry-{i:D2}", $"memory-{i:D2}")))
            .ToArray();

        await Task.WhenAll(tasks);

        var history = store.ReadHistory();
        for (var i = 0; i < 40; i++)
            Assert.Contains($"entry-{i:D2}", history, StringComparison.Ordinal);

        Assert.All(tasks.Select(t => t.Result), result => Assert.True(result.AnyWritten));
        Assert.StartsWith("memory-", store.ReadLongTerm(), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveConsolidation_ReportsSkippedWhenNothingChanged()
    {
        var store = new MemoryStore(_tempDir);
        store.WriteLongTerm("stable");

        var result = store.SaveConsolidation(null, "stable");

        Assert.False(result.AnyWritten);
        Assert.False(result.MemoryWritten);
        Assert.False(result.HistoryWritten);
    }

    [Fact]
    public void ClearAll_RemovesMemoryContentsAndPreservesRoot()
    {
        var store = new MemoryStore(_tempDir);
        store.WriteLongTerm("remember this");
        store.AppendHistory("history entry");
        Directory.CreateDirectory(Path.Combine(store.MemoryDirectoryPath, "derived"));
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, "derived", "summary.md"), "derived");
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, ".MEMORY.stale.tmp"), "tmp");

        store.ClearAll();

        Assert.True(Directory.Exists(store.MemoryDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.MemoryDirectoryPath));
        Assert.Equal(string.Empty, store.ReadLongTerm());
        Assert.Equal(string.Empty, store.ReadHistory());
    }

    [Fact]
    public void ClearAll_AllowsMemoryToBeRecreated()
    {
        var store = new MemoryStore(_tempDir);
        store.WriteLongTerm("old");
        store.AppendHistory("old history");

        store.ClearAll();

        store.WriteLongTerm("new");
        store.AppendHistory("new history");

        Assert.Equal("new", store.ReadLongTerm());
        Assert.Contains("new history", store.ReadHistory(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearAll_RejectsReparsePointRoot()
    {
        var target = Path.Combine(_tempDir, "target");
        var linkParent = Path.Combine(_tempDir, "linkParent");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(linkParent);
        var link = Path.Combine(linkParent, "memory");

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var store = new MemoryStore(linkParent);

        Assert.Throws<IOException>(() => store.ClearAll());
    }
}
