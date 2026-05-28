using DotCraft.Dreams;

namespace DotCraft.Tests.Dreams;

public sealed class DreamStoreTests : IDisposable
{
    private readonly string _tempDir;

    public DreamStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DreamStore_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { }
    }

    [Fact]
    public void ReadDream_WhenMissing_ReturnsEmpty()
    {
        var store = new DreamStore(_tempDir);

        Assert.Equal(string.Empty, store.ReadDream());
        Assert.Equal(Path.Combine(_tempDir, "dreams"), store.DreamsDirectoryPath);
    }

    [Fact]
    public void SaveDreamRun_WritesAppliedStoreUnderDreamsDirectory()
    {
        var store = new DreamStore(_tempDir);

        var result = store.SaveDreamRun("# Dream Memory\n", "dream run refreshed workspace focus");

        Assert.True(result.DreamWritten);
        Assert.False(result.HistoryWritten);
        Assert.Contains("/INDEX.md", string.Join("/", result.WrittenPaths), StringComparison.Ordinal);
        Assert.Equal("# Dream Memory\n", NormalizeNewlines(store.ReadDream()));
        Assert.False(string.IsNullOrWhiteSpace(store.GetActiveStoreId()));
        Assert.True(Directory.Exists(store.StoresDirectoryPath));
    }

    [Fact]
    public void OutputStore_DoesNotAffectActiveDreamUntilApplied()
    {
        var store = new DreamStore(_tempDir);
        store.SaveDreamRun("# Dream Memory\n", null);

        var output = store.CreateOutputStore("dream_test", DateTimeOffset.UtcNow);
        File.WriteAllText(output.IndexPath, "# Dream Memory\n\n- pending");

        Assert.DoesNotContain("pending", store.ReadDream(), StringComparison.Ordinal);
        store.SetActiveStore(output.StoreId);
        Assert.Contains("pending", store.ReadDream(), StringComparison.Ordinal);
    }

    [Fact]
    public void SaveDreamRun_WritesTopicFilesAndRejectsUnsafePaths()
    {
        var store = new DreamStore(_tempDir);

        var result = store.SaveDreamRun(
            "# Dream Memory\n\n- See memory/api-conventions.md",
            [new DreamTopicFileWrite { Path = "api-conventions.md", Content = "# API Conventions\nUse typed clients." }],
            null,
            "dream run refreshed topic files");

        Assert.True(result.DreamWritten);
        Assert.Equal(1, result.TopicFilesWritten);
        Assert.Contains("memory/api-conventions.md", result.WrittenPaths.Single(path => path.Contains("api-conventions", StringComparison.Ordinal)));
        Assert.Equal("# API Conventions\nUse typed clients.\n", NormalizeNewlines(store.ReadTopicFile("api-conventions.md")));

        var ex = Assert.Throws<ArgumentException>(() => store.SaveDreamRun(
            "# Dream Memory\n\n- unsafe",
            [new DreamTopicFileWrite { Path = "../unsafe.md", Content = "bad" }],
            null,
            null));
        Assert.Contains("safe top-level", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearAll_RemovesDreamsContentsAndPreservesRoot()
    {
        var store = new DreamStore(_tempDir);
        store.SaveDreamRun("# Dream Memory\n", "history entry");
        File.WriteAllText(Path.Combine(store.DreamsDirectoryPath, "state.json"), "{}");
        Directory.CreateDirectory(Path.Combine(store.DreamsDirectoryPath, "derived"));
        File.WriteAllText(Path.Combine(store.DreamsDirectoryPath, "derived", "snapshot.json"), "{}");

        store.ClearAll();

        Assert.True(Directory.Exists(store.DreamsDirectoryPath));
        Assert.Equal(
            ["runs", "stores"],
            Directory.EnumerateDirectories(store.DreamsDirectoryPath)
                .Select(path => Path.GetFileName(path)!)
                .OrderBy(static name => name)
                .ToArray());
        Assert.Equal(string.Empty, store.ReadDream());
    }

    private static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n");
}
