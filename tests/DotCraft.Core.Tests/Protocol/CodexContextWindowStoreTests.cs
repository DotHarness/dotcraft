using DotCraft.Protocol;
using DotCraft.State;

namespace DotCraft.Tests.Protocol;

public sealed class CodexContextWindowStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-codex-window-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; SQLite WAL files can remain briefly locked on Windows.
        }
    }

    [Fact]
    public async Task GetOrCreateIsStableAndAdvancePreservesPreviousWindow()
    {
        var runtime = new StateRuntime(_root);
        var threadStore = new ThreadStore(_root, runtime);
        var thread = new SessionThread
        {
            Id = "thread_codex_window",
            WorkspacePath = _root,
            OriginChannel = "test",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
        };
        await threadStore.SaveThreadAsync(thread);
        var store = new CodexContextWindowStore(runtime);

        var initial = store.GetOrCreate(thread.Id);
        var loadedAgain = store.GetOrCreate(thread.Id);
        var advanced = store.Advance(thread.Id);
        var loadedAdvanced = store.GetOrCreate(thread.Id);

        Assert.Equal(initial.CurrentWindowId, loadedAgain.CurrentWindowId);
        Assert.Equal(0, initial.Generation);
        Assert.Equal(initial.FirstWindowId, loadedAgain.FirstWindowId);
        Assert.Equal(initial.FirstWindowId, advanced.FirstWindowId);
        Assert.Equal(initial.CurrentWindowId, advanced.PreviousWindowId);
        Assert.NotEqual(initial.CurrentWindowId, advanced.CurrentWindowId);
        Assert.Equal(1, advanced.Generation);
        Assert.Equal(advanced.CurrentWindowId, loadedAdvanced.CurrentWindowId);
        Assert.Equal(advanced.PreviousWindowId, loadedAdvanced.PreviousWindowId);
    }
}
