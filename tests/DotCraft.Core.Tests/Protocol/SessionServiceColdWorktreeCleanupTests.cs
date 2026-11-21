using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceColdWorktreeCleanupTests : SessionServiceWorktreeTestBase
{
    [Theory]
    [InlineData("current", false, false)]
    [InlineData("missing", false, true)]
    [InlineData("stale", true, true)]
    public async Task ColdLifecycle_CleansWorktreeOnlyWithVerifiedBinding(
        string summaryState, bool delete, bool worktreeRemains)
    {
        await using var factory = CreateAgentFactory();
        string threadId;
        ThreadWorktreeInfo worktree;
        await using (var seed = CreateService(factory))
        {
            var thread = await seed.CreateThreadAsync(MakeIdentity());
            threadId = thread.Id;
            worktree = await HandoffAsync(seed, threadId, "dotcraft/cold-checkout");
        }

        if (summaryState != "current")
        {
            using var connection = _store.StateDatabase.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = summaryState == "missing"
                ? "DELETE FROM threads WHERE thread_id = $id"
                : "UPDATE threads SET projected_rollout_offset = 0 WHERE thread_id = $id";
            command.Parameters.AddWithValue("$id", threadId);
            command.ExecuteNonQuery();
        }

        await using var service = CreateService(factory);
        if (delete)
        {
            await service.DeleteThreadPermanentlyAsync(threadId);

            Assert.False(_store.ThreadExists(threadId));
        }
        else
        {
            Assert.True(await service.ArchiveThreadAsync(threadId));
        }

        Assert.Equal(worktreeRemains, Directory.Exists(worktree.Path));
        Assert.Null(service.TryGetLoadedThread(threadId));
    }
}
