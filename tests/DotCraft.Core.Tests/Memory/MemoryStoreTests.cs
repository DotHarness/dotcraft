using DotCraft.Memory;
using DotCraft.Sessions;
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
        catch { }
    }

    [Fact]
    public void ForThread_GivesEachScopeItsOwnStoreAndRefusesAnythingButOneSegment()
    {
        var workspace = new MemoryStore(_tempDir);
        var overrideRoot = Path.Combine(_tempDir, "other-workspace");

        Assert.Same(workspace, MemoryScopes.ForThread(null, workspace, _tempDir));
        Assert.Same(workspace, MemoryScopes.ForThread(new ThreadConfiguration { MemoryScope = "" }, workspace, _tempDir));

        var scoped = new ThreadConfiguration { MemoryScope = "agent-a", WorkspaceOverride = overrideRoot };
        Assert.Equal(Path.Combine(_tempDir, "scopes", "agent-a", "memory"),
            MemoryScopes.ForThread(scoped, workspace, _tempDir).MemoryDirectoryPath);
        Assert.Equal(MemoryScopes.ForThread(scoped, workspace, _tempDir).MemoryDirectoryPath,
            MemoryScopes.StoreDirectory(scoped, _tempDir));

        var overridden = new ThreadConfiguration { WorkspaceOverride = overrideRoot };
        Assert.Equal(Path.Combine(overrideRoot, Path.GetFileName(_tempDir), "memory"),
            MemoryScopes.ForThread(overridden, workspace, _tempDir).MemoryDirectoryPath);

        foreach (var refused in new[] { "..", "a/b", "a\\b" })
        {
            var config = new ThreadConfiguration { MemoryScope = refused };
            Assert.Throws<ArgumentException>(() => MemoryScopes.ForThread(config, workspace, _tempDir));
        }
    }

    [Fact]
    public void ClearAll_EmptiesEveryStoreButKeepsRootsAndSiblings()
    {
        var workspace = new MemoryStore(_tempDir);
        var nightly = MemoryScopes.ForThread(new ThreadConfiguration { MemoryScope = "nightly" }, workspace, _tempDir);
        var agent = MemoryScopes.ForThread(new ThreadConfiguration { MemoryScope = "agent-a" }, workspace, _tempDir);
        File.WriteAllText(workspace.IndexFilePath, "- [A](a.md) — a");
        File.WriteAllText(Path.Combine(nightly.MemoryDirectoryPath, "fact.md"), "fact");
        File.WriteAllText(agent.IndexFilePath, "- [B](b.md) — b");
        var scopeSibling = Path.Combine(_tempDir, "scopes", "agent-a", "notes.txt");
        File.WriteAllText(scopeSibling, "not memory");

        MemoryScopes.ClearAll(workspace);

        foreach (var store in new[] { workspace, nightly, agent })
        {
            Assert.True(Directory.Exists(store.MemoryDirectoryPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(store.MemoryDirectoryPath));
        }

        Assert.True(File.Exists(scopeSibling));

        var cleared = new MemoryStore(_tempDir);
        File.WriteAllText(cleared.IndexFilePath, "remember this");
        File.WriteAllText(Path.Combine(cleared.MemoryDirectoryPath, "HISTORY.md"), "legacy history");
        Directory.CreateDirectory(Path.Combine(cleared.MemoryDirectoryPath, "derived"));
        File.WriteAllText(Path.Combine(cleared.MemoryDirectoryPath, "derived", "summary.md"), "derived");
        File.WriteAllText(Path.Combine(cleared.MemoryDirectoryPath, ".MEMORY.stale.tmp"), "tmp");

        cleared.ClearAll();

        Assert.True(Directory.Exists(cleared.MemoryDirectoryPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cleared.MemoryDirectoryPath));
        Assert.Equal(string.Empty, cleared.ReadIndexText());
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
