using System.Text;
using DotCraft.Memory;
using Xunit;

namespace DotCraft.Tests.Memory;

public sealed class MemoryIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "MemoryIndex_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly MemoryStore _store;

    public MemoryIndexTests()
    {
        Directory.CreateDirectory(_root);
        _store = new MemoryStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Load_MissingOrBlankIndex_IsEmpty()
    {
        Assert.True(MemoryIndex.Load(_store.MemoryDirectoryPath).IsEmpty);

        File.WriteAllText(_store.IndexFilePath, "\n  \n");

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);
        Assert.True(index.IsEmpty);
        Assert.Empty(index.Notices);
        Assert.Empty(index.Lines);
    }

    [Fact]
    public void Load_PointerIndex_KeepsEveryLineAndNamesEachTarget()
    {
        File.WriteAllText(_store.IndexFilePath,
            "- [Branches](no-branches.md) — never branch unasked\r\n\r\n* [Release](./release-notes.md) — 0.8.6 copy\n");

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.Empty(index.Notices);
        Assert.Equal(["no-branches.md", "release-notes.md"], index.Lines.Select(static line => line.Target));
        Assert.Equal(index.Content, index.Text);
    }

    [Fact]
    public void Load_OverTheLineLimit_KeepsTheLeadingLinesAndAddsOneNotice()
    {
        var lines = Enumerable.Range(1, MemoryIndex.MaxLines + 50).Select(static i => $"- [M{i}](m{i}.md) — hook {i}");
        File.WriteAllText(_store.IndexFilePath, string.Join('\n', lines));

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.Equal(MemoryIndex.MaxLines, index.Lines.Count);
        Assert.Equal($"m{MemoryIndex.MaxLines}.md", index.Lines[^1].Target);
        Assert.Single(index.Notices);
        Assert.EndsWith(index.Notices[0], index.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_OverTheByteLimit_CutsAtALineBoundary()
    {
        var line = "- [Wide](wide.md) — " + new string('é', 90);
        var lines = Enumerable.Range(0, 150).Select(i => line.Replace("wide.md", $"wide{i}.md")).ToArray();
        File.WriteAllText(_store.IndexFilePath, string.Join('\n', lines));
        Assert.True(Encoding.UTF8.GetByteCount(string.Join('\n', lines)) > MemoryIndex.MaxBytes);

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.True(Encoding.UTF8.GetByteCount(index.Content) <= MemoryIndex.MaxBytes);
        Assert.All(index.Lines, loaded => Assert.Contains(loaded.Text, lines));
        Assert.True(index.Lines.Count < lines.Length);
        Assert.Single(index.Notices);
    }

    [Fact]
    public void Load_ASingleLineOverTheByteLimit_NeverSplitsACharacter()
    {
        File.WriteAllText(_store.IndexFilePath, new string('é', MemoryIndex.MaxBytes));

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.True(Encoding.UTF8.GetByteCount(index.Content) <= MemoryIndex.MaxBytes);
        Assert.All(index.Content, static c => Assert.Equal('é', c));
    }

    [Theory]
    [InlineData("Prefer focused tests.")]
    [InlineData("## User preferences")]
    public void Load_ContentThatIsNotAPointer_AddsTheMigrationNotice(string line)
    {
        File.WriteAllText(_store.IndexFilePath, $"- [A](a.md) — a\n{line}");

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.Single(index.Notices);
        Assert.Contains(index.Lines, loaded => loaded.Text == line && loaded.Target == null);
    }

    [Fact]
    public void Load_APointerLongerThanTwoHundredCharacters_AddsTheMigrationNotice()
    {
        File.WriteAllText(_store.IndexFilePath, "- [Long](long.md) — " + new string('x', 200));

        var index = MemoryIndex.Load(_store.MemoryDirectoryPath);

        Assert.Single(index.Notices);
        Assert.Equal("long.md", Assert.Single(index.Lines).Target);
    }

    [Theory]
    [InlineData(160, 0, false)]
    [InlineData(161, 0, true)]
    [InlineData(10, 20_001, true)]
    [InlineData(201, 0, true)]
    public void WriteNotice_FiresBeyondEightyPercentOfEitherLimit(int lineCount, int padBytes, bool expected)
    {
        var lines = Enumerable.Range(1, lineCount).Select(static i => $"- [M{i}](m{i}.md)").ToList();
        lines[0] += new string('x', padBytes);

        var notice = MemoryIndex.WriteNotice(_store.MemoryDirectoryPath, _store.IndexFilePath, string.Join('\n', lines));

        Assert.Equal(expected, notice != null);
    }

    [Fact]
    public void WriteNotice_IgnoresFilesOtherThanTheStoreIndex()
    {
        var oversized = string.Join('\n', Enumerable.Range(1, 300).Select(static i => $"line {i}"));
        var elsewhere = Path.Combine(_root, "MEMORY.md");

        Assert.Null(MemoryIndex.WriteNotice(_store.MemoryDirectoryPath, Path.Combine(_store.MemoryDirectoryPath, "fact.md"), oversized));
        Assert.Null(MemoryIndex.WriteNotice(_store.MemoryDirectoryPath, elsewhere, oversized));
    }
}
