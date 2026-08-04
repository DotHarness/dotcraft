using DotCraft.Protocol;
using DotCraft.InlineVisualizations;
using DotCraft.Sessions;
using SessionItem = DotCraft.Sessions.SessionItem;
using SessionThread = DotCraft.Sessions.SessionThread;
using SessionTurn = DotCraft.Sessions.SessionTurn;
using AgentMessagePayload = DotCraft.Sessions.AgentMessagePayload;
using Xunit;

namespace DotCraft.Core.Tests.InlineVisualizations;

public sealed class InlineVisualizationAssetStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-inline-visualization-tests", Guid.NewGuid().ToString("N"));

    public InlineVisualizationAssetStoreTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void EnsureAuthoringDirectory_ReturnsThreadScopedRoot()
    {
        var store = new InlineVisualizationAssetStore();
        var thread = CreateCompletedMessage("message", _root).Thread;

        var directory = store.EnsureAuthoringDirectory(thread);

        Assert.Equal(Path.Combine(_root, ".craft", "visualizations", "thread_test"), directory);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_ValidatesDirectiveAndReturnsFileContents()
    {
        var store = new InlineVisualizationAssetStore();
        var (thread, turn, item) = CreateCompletedMessage(
            "::dotcraft-inline-vis{file=\"chart.html\"}",
            _root);
        var directory = store.EnsureAuthoringDirectory(thread);
        await File.WriteAllTextAsync(Path.Combine(directory, "chart.html"), "<div>ok</div>");

        var result = await store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html");

        Assert.Equal("<div>ok</div>", result);
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_DoesNotEnforceSkillAuthoringGuidance()
    {
        var store = new InlineVisualizationAssetStore();
        var (thread, turn, item) = CreateCompletedMessage(
            "::dotcraft-inline-vis{file=\"chart.html\"}",
            _root);
        var directory = store.EnsureAuthoringDirectory(thread);
        const string source = "<!doctype html><html><body><div class=\\\"chart\\\">ok</div>\\n</body></html>";
        await File.WriteAllTextAsync(Path.Combine(directory, "chart.html"), source);

        var result = await store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html");

        Assert.Equal(source, result);
    }

    [Theory]
    [InlineData("plain text", "chart.html", "not_referenced")]
    [InlineData("::dotcraft-inline-vis{file=\"chart.html\"}", "../chart.html", "not_referenced")]
    public async Task ReadReferencedFragmentAsync_RejectsUnauthorizedFiles(string text, string file, string code)
    {
        var store = new InlineVisualizationAssetStore();
        var (thread, turn, item) = CreateCompletedMessage(text, _root);

        var error = await Assert.ThrowsAsync<InlineVisualizationException>(() =>
            store.ReadReferencedFragmentAsync(thread, turn, item, file));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task Workspaces_WithSameThreadId_AreStrictlyIsolated()
    {
        var workspaceA = Path.Combine(_root, "workspace-a");
        var workspaceB = Path.Combine(_root, "workspace-b");
        Directory.CreateDirectory(workspaceA);
        Directory.CreateDirectory(workspaceB);
        var store = new InlineVisualizationAssetStore();
        var message = "::dotcraft-inline-vis{file=\"chart.html\"}";
        var (threadA, _, _) = CreateCompletedMessage(message, workspaceA);
        var (threadB, turnB, itemB) = CreateCompletedMessage(message, workspaceB);
        var directoryA = store.EnsureAuthoringDirectory(threadA);
        var directoryB = store.EnsureAuthoringDirectory(threadB);
        await File.WriteAllTextAsync(Path.Combine(directoryA, "chart.html"), "<div>workspace a</div>");

        Assert.NotEqual(directoryA, directoryB);
        var error = await Assert.ThrowsAsync<InlineVisualizationException>(() =>
            store.ReadReferencedFragmentAsync(threadB, turnB, itemB, "chart.html"));
        Assert.Equal("not_found", error.Code);
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
        var store = new InlineVisualizationAssetStore();

        var error = Assert.Throws<InlineVisualizationException>(() => store.GetAuthoringDirectory(thread));

        Assert.Equal(code, error.Code);
    }

    [Fact]
    public void EnsureAuthoringDirectory_RejectsReparsePointWorkspace()
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
        var store = new InlineVisualizationAssetStore();

        var error = Assert.Throws<InlineVisualizationException>(() => store.EnsureAuthoringDirectory(thread));

        Assert.Equal("unsafe_path", error.Code);
    }

    [Fact]
    public async Task ReadReferencedFragmentAsync_RejectsReparsePointFile()
    {
        var store = new InlineVisualizationAssetStore();
        var (thread, turn, item) = CreateCompletedMessage(
            "::dotcraft-inline-vis{file=\"chart.html\"}",
            _root);
        var directory = store.EnsureAuthoringDirectory(thread);
        var target = Path.Combine(_root, "target.html");
        var link = Path.Combine(directory, "chart.html");
        await File.WriteAllTextAsync(target, "<div>target</div>");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        var error = await Assert.ThrowsAsync<InlineVisualizationException>(() =>
            store.ReadReferencedFragmentAsync(thread, turn, item, "chart.html"));

        Assert.Equal("unsafe_path", error.Code);
    }

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

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
