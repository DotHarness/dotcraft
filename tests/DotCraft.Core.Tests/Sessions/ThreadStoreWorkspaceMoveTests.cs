using DotCraft.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DotCraft.Tests.Sessions;

public sealed class ThreadStoreWorkspaceMoveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "dotcraft-workspace-move-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Threads_follow_a_renamed_workspace()
    {
        var original = Path.Combine(_root, "before");
        var renamed = Path.Combine(_root, "after");
        var now = DateTimeOffset.UtcNow;
        await using (var store = new ThreadStore(Path.Combine(original, ".craft")))
        {
            await store.SaveThreadAsync(new SessionThread
            {
                Id = "thread_moved",
                WorkspacePath = original,
                OriginChannel = "test",
                Status = ThreadStatus.Active,
                HistoryMode = HistoryMode.Server,
                CreatedAt = now,
                LastActiveAt = now
            });
            await store.LoadIndexAsync();
        }
        SqliteConnection.ClearAllPools();

        Directory.Move(original, renamed);

        await using var movedStore = new ThreadStore(Path.Combine(renamed, ".craft"));
        var summary = Assert.Single(await movedStore.LoadIndexAsync());
        Assert.Equal(Path.GetFullPath(renamed), summary.WorkspacePath);
        var thread = await movedStore.LoadThreadAsync("thread_moved");
        Assert.Equal(Path.GetFullPath(renamed), thread!.WorkspacePath);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
