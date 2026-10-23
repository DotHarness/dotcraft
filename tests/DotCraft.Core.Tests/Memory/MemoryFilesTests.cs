using DotCraft.Memory;
using Xunit;

namespace DotCraft.Tests.Memory;

public sealed class MemoryFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MemoryFiles_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly MemoryStore _store;

    public MemoryFilesTests()
    {
        Directory.CreateDirectory(_root);
        _store = new MemoryStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    [Fact]
    public void List_ReadsFrontmatterTolerantlyAndSkipsEverythingButTopLevelMemoryFiles()
    {
        var now = DateTime.UtcNow;
        Write("typed.md", "---\nname: typed\ndescription: \"Prefers focused tests\"\nmetadata:\n  type: feedback\n---\n\nBody.", now);
        Write("plain.md", "No frontmatter at all.", now.AddMinutes(-1));
        Write("unknown.md", "---\nname: unknown\ndescription: Odd type\nmetadata:\n  type: diary\n---\nBody", now.AddMinutes(-2));
        Write("broken.md", "---\ndescription: [unclosed\n---\nBody", now.AddMinutes(-3));
        Write("unclosed.md", "---\ndescription: never closed\n", now.AddMinutes(-4));
        Write(MemoryStore.IndexFileName, "- [Typed](typed.md) — hook", now);
        Write("notes.txt", "not markdown", now);
        Directory.CreateDirectory(Path.Combine(_store.MemoryDirectoryPath, "nested"));
        Write(Path.Combine("nested", "deep.md"), "---\ndescription: nested\n---\n", now);

        var files = MemoryFiles.List(_store.MemoryDirectoryPath);

        Assert.Equal(["typed.md", "plain.md", "unknown.md", "broken.md", "unclosed.md"], files.Select(static f => f.FileName));
        Assert.Equal(["feedback", null, null, null, null], files.Select(static f => f.Type));
        Assert.Equal(["Prefers focused tests", null, "Odd type", null, null], files.Select(static f => f.Description));
    }

    [Fact]
    public void List_KeepsTheNewestTwoHundred()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < MemoryFiles.MaxListed + 5; i++)
            Write($"m{i:D3}.md", "body", now.AddMinutes(-i));

        var files = MemoryFiles.List(_store.MemoryDirectoryPath);

        Assert.Equal(MemoryFiles.MaxListed, files.Count);
        Assert.Equal("m000.md", files[0].FileName);
        Assert.DoesNotContain(files, static f => f.FileName == $"m{MemoryFiles.MaxListed:D3}.md");
    }

    private void Write(string relativePath, string content, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_store.MemoryDirectoryPath, relativePath);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }
}
