using System.Text.Json;
using DotCraft.Visualizations;
using DotCraft.Sessions;
using DotCraft.Workspaces;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using Xunit;

namespace DotCraft.Core.Tests.Visualizations;

public sealed class VisualizationAssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-visualization-store-tests", Guid.NewGuid().ToString("N"));

    public VisualizationAssetStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_ValidatesReferenceAndReturnsCurrentFileContents()
    {
        var store = CreateAssetStore();
        const string fileName = "Sales_报告 v2.HTML";
        var path = Path.Combine(Path.GetDirectoryName(ChartPath(_root))!, fileName);
        var (thread, turn, item) = CreateCompletedMessage(Reference(fileName), _root);
        Directory.CreateDirectory(store.GetAuthoringDirectory(thread));
        await File.WriteAllTextAsync(path, "<div>ok</div>");

        Assert.Equal("<div>ok</div>", await store.ReadReferencedFragmentAsync(thread, turn, item, fileName));

        await File.WriteAllTextAsync(path, "<div>updated</div>");
        Assert.Equal("<div>updated</div>", await store.ReadReferencedFragmentAsync(thread, turn, item, fileName));
        var error = await Assert.ThrowsAsync<VisualizationException>(() =>
            store.ReadReferencedFragmentAsync(thread, turn, item, fileName.ToLowerInvariant()));
        Assert.Equal("not_referenced", error.Code);
    }

    [Theory]
    [InlineData("x", 1_000_000, true)]
    [InlineData("x", 1_000_001, false)]
    [InlineData("界", 333_334, false)]
    public async Task ReadReferencedFragmentAsync_EnforcesFragmentByteLimit(string value, int count, bool allowed)
    {
        var store = CreateAssetStore();
        var path = ChartPath(_root);
        var (thread, turn, item) = CreateCompletedMessage(Reference("chart.html"), _root);
        Directory.CreateDirectory(store.GetAuthoringDirectory(thread));
        var fragment = string.Concat(Enumerable.Repeat(value, count));
        await File.WriteAllTextAsync(path, fragment);

        if (allowed)
        {
            Assert.Equal(fragment, await store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html"));
        }
        else
        {
            var error = await Assert.ThrowsAsync<VisualizationException>(() =>
                store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html"));
            Assert.Equal("fragment_too_large", error.Code);
        }
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_RejectsUnreferencedPaths()
    {
        var store = CreateAssetStore();
        foreach (var (text, requested) in new[] { ("plain text", "chart.html"), (Reference("chart.html"), "other.html") })
        {
            var (thread, turn, item) = CreateCompletedMessage(text, _root);
            var error = await Assert.ThrowsAsync<VisualizationException>(() =>
                store.ReadReferencedFragmentAsync(thread, turn, item, requested));
            Assert.Equal("not_referenced", error.Code);
        }
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_RejectsReferencesOutsideTheThreadDirectory()
    {
        var store = CreateAssetStore();
        var outside = Path.Combine(_root, ".craft", "visualizations", "chart.html");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        await File.WriteAllTextAsync(outside, "<div>outside</div>");

        foreach (var path in new[] { "../chart.html", @"..\chart.html", "nested/chart.html", outside })
        {
            var (thread, turn, item) = CreateCompletedMessage(Reference(path), _root);
            var error = await Assert.ThrowsAsync<VisualizationException>(() =>
                store.ReadReferencedFragmentAsync(thread, turn, item, path));

            Assert.Equal("not_referenced", error.Code);
        }
    }

    [Fact]
    public async Task Workspaces_WithSameThreadId_AreStrictlyIsolated()
    {
        var workspaceA = Path.Combine(_root, "workspace-a");
        var workspaceB = Path.Combine(_root, "workspace-b");
        Directory.CreateDirectory(workspaceA);
        Directory.CreateDirectory(workspaceB);
        var store = CreateAssetStore();
        var pathA = ChartPath(workspaceA);
        var pathB = ChartPath(workspaceB);
        var (threadA, _, _) = CreateCompletedMessage(Reference("chart.html"), workspaceA);
        var (threadB, turnB, itemB) = CreateCompletedMessage(Reference("chart.html"), workspaceB);
        var directoryA = Directory.CreateDirectory(store.GetAuthoringDirectory(threadA)).FullName;
        var directoryB = Directory.CreateDirectory(store.GetAuthoringDirectory(threadB)).FullName;
        await File.WriteAllTextAsync(pathA, "<div>workspace a</div>");

        Assert.NotEqual(directoryA, directoryB);
        var missing = await Assert.ThrowsAsync<VisualizationException>(() =>
            store.ReadReferencedFragmentAsync(threadB, turnB, itemB, "chart.html"));
        Assert.Equal("not_found", missing.Code);
        await File.WriteAllTextAsync(pathB, "<div>workspace b</div>");
        Assert.Equal("<div>workspace b</div>", await store.ReadReferencedFragmentAsync(threadB, turnB, itemB, "chart.html"));
    }

    [Theory]
    [InlineData("relative-workspace", "thread_test", "unsafe_path")]
    [InlineData("{missing}", "thread_test", "not_found")]
    [InlineData("{root}", "../thread_test", "unsafe_path")]
    [InlineData("{root}", "thread/test", "unsafe_path")]
    public void GetAuthoringDirectory_RejectsUnsafeWorkspaceOrThread(
        string workspaceValue,
        string threadId,
        string code)
    {
        var workspace = workspaceValue switch
        {
            "{root}" => _root,
            "{missing}" => Path.Combine(_root, "missing"),
            _ => workspaceValue
        };
        var thread = CreateCompletedMessage("message", workspace, threadId).Thread;
        var store = CreateAssetStore();

        var error = Assert.Throws<VisualizationException>(() => store.GetAuthoringDirectory(thread));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void GetAuthoringDirectory_RejectsReparsePointWorkspace()
    {
        var target = Path.Combine(_root, "target-workspace");
        var link = Path.Combine(_root, "linked-workspace");
        Directory.CreateDirectory(target);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }
        var thread = CreateCompletedMessage("message", link).Thread;
        var store = CreateAssetStore();

        var error = Assert.Throws<VisualizationException>(() => store.GetAuthoringDirectory(thread));

        Assert.Equal("unsafe_path", error.Code);
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_RejectsReparsePointFile()
    {
        var store = CreateAssetStore();
        var path = ChartPath(_root);
        var (thread, turn, item) = CreateCompletedMessage(Reference("chart.html"), _root);
        Directory.CreateDirectory(store.GetAuthoringDirectory(thread));
        var target = Path.Combine(_root, "target.html");
        await File.WriteAllTextAsync(target, "<div>target</div>");
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var error = await Assert.ThrowsAsync<VisualizationException>(() =>
            store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html"));

        Assert.Equal("unsafe_path", error.Code);
    }

    [Fact]
    public void DeleteThreadDirectory_DoesNotFollowAReparsePointThreadDirectory()
    {
        var target = Path.Combine(_root, "outside");
        Directory.CreateDirectory(target);
        var kept = Path.Combine(target, "kept.html");
        File.WriteAllText(kept, "<div>kept</div>");
        var link = Path.Combine(_root, ".craft", "visualizations", "thread_test");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var result = VisualizationAssetStore.DeleteThreadDirectory(_root, Path.Combine(_root, ".craft"), "thread_test");

        Assert.Equal(1, result.Errors);
        Assert.True(File.Exists(kept));
    }

    private static string ChartPath(string workspace) =>
        Path.Combine(workspace, ".craft", "visualizations", "thread_test", "chart.html");

    private static string Reference(string path) =>
        $"visualize{JsonSerializer.Serialize(new { path })}";

    private static (SessionThread Thread, SessionTurn Turn, SessionItem Item) CreateCompletedMessage(
        string text,
        string workspacePath,
        string threadId = "thread_test")
    {
        var now = DateTimeOffset.UtcNow;
        var thread = new SessionThread { Id = threadId, CreatedAt = now, WorkspacePath = workspacePath };
        var item = new SessionItem
        {
            Id = "item_agent",
            TurnId = "turn_test",
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = now,
            CompletedAt = now,
            Payload = new AgentMessagePayload { Text = text }
        };
        var turn = new SessionTurn
        {
            Id = "turn_test",
            ThreadId = thread.Id,
            Status = TurnStatus.Completed,
            Items = [item],
            StartedAt = now,
            CompletedAt = now
        };
        thread.Turns.Add(turn);
        return (thread, turn, item);
    }

    private VisualizationAssetStore CreateAssetStore() =>
        new(new DotCraftPaths(_root, Path.Combine(_root, ".craft"), userDataPath: null));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
