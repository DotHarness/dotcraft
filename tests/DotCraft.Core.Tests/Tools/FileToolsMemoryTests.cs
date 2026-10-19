using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Tools;

public sealed class FileToolsMemoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-filetools-memory", Guid.NewGuid().ToString("N"));
    private readonly string _worktree;
    private readonly MemoryStore _store;

    public FileToolsMemoryTests()
    {
        _worktree = Path.Combine(_root, "worktree");
        Directory.CreateDirectory(_worktree);
        _store = new MemoryStore(Path.Combine(_root, "workspace", ".craft"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task ReadFile_MemoryFileOlderThanADay_EndsWithTheAgeNote()
    {
        var stale = WriteMemoryFile("stale.md", DateTime.UtcNow.AddDays(-3));
        var fresh = WriteMemoryFile("fresh.md", DateTime.UtcNow.AddHours(-30));

        var plain = await ReadAsync(new FileTools(_worktree, requireApprovalOutsideWorkspace: true), stale);
        var noted = await ReadAsync(MemoryAwareTools(), stale);

        Assert.StartsWith(plain, noted, StringComparison.Ordinal);
        Assert.True(noted.Length > plain.Length);
        Assert.Contains("3", noted[plain.Length..], StringComparison.Ordinal);
        Assert.Equal(
            await ReadAsync(new FileTools(_worktree, requireApprovalOutsideWorkspace: true), fresh),
            await ReadAsync(MemoryAwareTools(), fresh));
    }

    [Fact]
    public async Task ReadFile_IndexAndFilesOutsideTheStore_GetNoAgeNote()
    {
        File.WriteAllText(_store.IndexFilePath, "- [A](a.md) — a");
        File.SetLastWriteTimeUtc(_store.IndexFilePath, DateTime.UtcNow.AddDays(-10));
        var outside = Path.Combine(_worktree, "notes.md");
        File.WriteAllText(outside, "workspace notes");
        File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-10));

        foreach (var path in new[] { _store.IndexFilePath, outside })
        {
            Assert.Equal(
                await ReadAsync(new FileTools(_worktree, requireApprovalOutsideWorkspace: true), path),
                await ReadAsync(MemoryAwareTools(), path));
        }
    }

    [Fact]
    public async Task WriteFile_IndexBeyondEightyPercentOfALimit_EndsWithTheCapacityNotice()
    {
        var plain = new FileTools(_worktree, requireApprovalOutsideWorkspace: true);
        var aware = MemoryAwareTools();
        var small = IndexLines(10);
        var large = IndexLines(170);
        var elsewhere = Path.Combine(_worktree, "MEMORY.md");

        Assert.Equal(
            (await plain.WriteFile(_store.IndexFilePath, small)).Content,
            (await aware.WriteFile(_store.IndexFilePath, small)).Content);
        Assert.Equal(
            (await plain.WriteFile(elsewhere, large)).Content,
            (await aware.WriteFile(elsewhere, large)).Content);

        var unwarned = (await plain.WriteFile(_store.IndexFilePath, large)).Content!;
        var warned = await aware.WriteFile(_store.IndexFilePath, large);
        Assert.True(warned.Success);
        Assert.StartsWith(unwarned, warned.Content, StringComparison.Ordinal);
        Assert.True(warned.Content!.Length > unwarned.Length);
    }

    [Fact]
    public async Task EditFile_IndexBeyondEightyPercentOfALimit_EndsWithTheCapacityNotice()
    {
        var lines = IndexLines(165).Split('\n');
        var oldText = lines[149];
        var newText = string.Join('\n', lines.Skip(149));

        File.WriteAllText(_store.IndexFilePath, string.Join('\n', lines.Take(150)));
        var unwarned = (await new FileTools(_worktree, requireApprovalOutsideWorkspace: true)
            .EditFile(_store.IndexFilePath, oldText, newText)).Content!;
        File.WriteAllText(_store.IndexFilePath, string.Join('\n', lines.Take(150)));
        var warned = await MemoryAwareTools().EditFile(_store.IndexFilePath, oldText, newText);

        Assert.True(warned.Success);
        Assert.StartsWith(unwarned, warned.Content, StringComparison.Ordinal);
        Assert.True(warned.Content!.Length > unwarned.Length);
    }

    [Fact]
    public async Task StoreOutsideTheWorkspace_IsWritableWhileOtherOutsidePathsStayRejected()
    {
        var tools = MemoryAwareTools(requireApprovalOutsideWorkspace: false);
        var memoryFile = Path.Combine(_store.MemoryDirectoryPath, "user-role.md");
        var outsideFile = Path.Combine(_root, "workspace", "src", "Program.cs");

        var written = await tools.WriteFile(memoryFile, "---\ndescription: role\n---\nBackend engineer.");
        var edited = await tools.EditFile(memoryFile, "Backend", "Platform");
        var read = await ReadAsync(tools, memoryFile);
        var rejected = await tools.WriteFile(outsideFile, "class Program {}");

        Assert.True(written.Success);
        Assert.True(edited.Success);
        Assert.Contains("Platform engineer.", read, StringComparison.Ordinal);
        Assert.False(rejected.Success);
        Assert.False(File.Exists(outsideFile));
    }

    private FileTools MemoryAwareTools(bool requireApprovalOutsideWorkspace = true) =>
        new(_worktree, requireApprovalOutsideWorkspace, memoryStores: new ThreadMemoryStores(_store, []));

    private static string IndexLines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(static i => $"- [M{i}](m{i}.md) — hook"));

    private string WriteMemoryFile(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_store.MemoryDirectoryPath, name);
        File.WriteAllText(path, "---\ndescription: fact\n---\nA fact.");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    private static async Task<string> ReadAsync(FileTools tools, string path) =>
        Assert.Single((await tools.ReadFile(path)).OfType<TextContent>()).Text;
}
