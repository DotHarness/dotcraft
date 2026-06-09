using DotCraft.Protocol;
using DotCraft.State;

namespace DotCraft.Tests.Protocol;

/// <summary>
/// Persistence contract for the Interactive Tool UI <c>widgetState</c> side store (M-iv):
/// last-write-wins upsert, clear, and cascade-delete with the owning thread.
/// </summary>
public sealed class ItemWidgetStateStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WidgetStateTests_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly StateRuntime _stateRuntime;
    private readonly ThreadStore _threadStore;

    public ItemWidgetStateStoreTests()
    {
        _stateRuntime = new StateRuntime(_root);
        _threadStore = new ThreadStore(_root, _stateRuntime);
    }

    [Fact]
    public async Task WidgetState_UpsertsLastWriteWinsLoadsAndClears()
    {
        var thread = CreateThread("thread_widget");
        await _threadStore.SaveThreadAsync(thread);

        _threadStore.SaveItemWidgetState(thread.Id, "call_1", "{\"tab\":1}");
        _threadStore.SaveItemWidgetState(thread.Id, "call_2", "{\"open\":true}");
        _threadStore.SaveItemWidgetState(thread.Id, "call_1", "{\"tab\":2}");

        var states = _threadStore.LoadItemWidgetStates(thread.Id);
        Assert.Equal(2, states.Count);
        Assert.Equal("{\"tab\":2}", states["call_1"]);
        Assert.Equal("{\"open\":true}", states["call_2"]);

        _threadStore.DeleteItemWidgetState(thread.Id, "call_1");
        var afterDelete = _threadStore.LoadItemWidgetStates(thread.Id);
        Assert.Single(afterDelete);
        Assert.False(afterDelete.ContainsKey("call_1"));
        Assert.Equal("{\"open\":true}", afterDelete["call_2"]);
    }

    [Fact]
    public async Task WidgetState_IsCascadeDeletedWithThread()
    {
        var thread = CreateThread("thread_widget_cascade");
        await _threadStore.SaveThreadAsync(thread);
        _threadStore.SaveItemWidgetState(thread.Id, "call_1", "{\"x\":1}");
        Assert.Single(_threadStore.LoadItemWidgetStates(thread.Id));

        _threadStore.DeleteThread(thread.Id);

        // Re-create the same thread id: the old widget state must not resurface (FK cascade-deleted).
        await _threadStore.SaveThreadAsync(CreateThread(thread.Id));
        Assert.Empty(_threadStore.LoadItemWidgetStates(thread.Id));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort on Windows.
        }
    }

    private static SessionThread CreateThread(string id) => new()
    {
        Id = id,
        WorkspacePath = Path.Combine(Path.GetTempPath(), "workspace"),
        UserId = "local",
        OriginChannel = "test",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };
}
