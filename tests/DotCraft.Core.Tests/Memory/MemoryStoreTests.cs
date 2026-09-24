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
    public void Resolve_GivesEachScopeItsOwnRootAndRefusesAnythingButOneSegment()
    {
        Assert.Null(MemoryScopes.Resolve(null, _tempDir));
        Assert.Null(MemoryScopes.Resolve("", _tempDir));
        Assert.Equal(Path.Combine(_tempDir, "scopes", "agent-a", "memory"),
            MemoryScopes.Resolve("agent-a", _tempDir)!.MemoryDirectoryPath);
        foreach (var refused in new[] { "..", "a/b", "a\\b" })
            Assert.Throws<ArgumentException>(() => MemoryScopes.Resolve(refused, _tempDir));
    }

    [Fact]
    public void GetMemoryContext_StopsAtTheBound_HoweverLargeTheFileGrew()
    {
        var store = new MemoryStore(_tempDir);
        File.WriteAllText(store.LongTermFilePath, new string('x', MemoryStore.MaxContextChars) + "must-not-reach-context");

        Assert.DoesNotContain("must-not-reach-context", store.GetMemoryContext(), StringComparison.Ordinal);
        Assert.Contains("must-not-reach-context", store.ReadLongTerm(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearAll_RemovesMemoryContentsAndPreservesRoot()
    {
        var store = new MemoryStore(_tempDir);
        File.WriteAllText(store.LongTermFilePath, "remember this");
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, "HISTORY.md"), "legacy history");
        Directory.CreateDirectory(Path.Combine(store.MemoryDirectoryPath, "derived"));
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, "derived", "summary.md"), "derived");
        File.WriteAllText(Path.Combine(store.MemoryDirectoryPath, ".MEMORY.stale.tmp"), "tmp");

        store.ClearAll();

        Assert.True(Directory.Exists(store.MemoryDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(store.MemoryDirectoryPath));
        Assert.Equal(string.Empty, store.ReadLongTerm());
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
