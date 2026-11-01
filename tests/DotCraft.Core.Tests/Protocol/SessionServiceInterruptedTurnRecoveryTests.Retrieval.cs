using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed partial class SessionServiceInterruptedTurnRecoveryTests
{
    [Fact]
    public async Task PassiveRetrieval_LeavesColdExecutionStateUnloadedAndUnchanged()
    {
        var (threadId, _, _, _) = await SeedThreadAsync(TurnStatus.WaitingApproval);
        var rolloutPath = new ThreadRolloutStore(CraftPath).ResolveExistingPath(threadId)!;
        var before = await File.ReadAllTextAsync(rolloutPath);
        using var client = new StaticChatClient("unused");
        await using var factory = CreateAgentFactory(client);
        ISessionService service = CreateService(factory, client);

        var page = await service.SearchThreadMessageOccurrencesAsync(threadId, "response", null, 20);
        var hit = Assert.Single(page.Data);
        Assert.Equal("turn_001", hit.TurnId);
        Assert.Equal("item_002", hit.ItemId);
        var items = await service.ReadThreadItemsAsync(
            threadId, hit.TurnId, [hit.ItemId, "item_001", hit.ItemId, "missing"]);

        Assert.Equal(["item_001", "item_002"], items.Select(entry => entry.Item.Id));
        Assert.Equal("partial response", items[1].Item.AsAgentMessage!.Text);
        Assert.Null(service.TryGetLoadedThread(threadId));
        Assert.Equal(before, await File.ReadAllTextAsync(rolloutPath));
        await using var store = new ThreadStore(CraftPath);
        var persisted = Assert.IsType<SessionThread>(await store.LoadThreadAsync(threadId));
        Assert.Equal(TurnStatus.WaitingApproval, Assert.Single(persisted.Turns).Status);
    }

    [Fact]
    public async Task PassiveRetrieval_RejectsEphemeralHistories()
    {
        using var client = new StaticChatClient("unused");
        await using var factory = CreateAgentFactory(client);
        var service = CreateService(factory, client);
        var thread = await service.CreateThreadAsync(CreateIdentity(), ephemeral: true);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.SearchThreadMessageOccurrencesAsync(thread.Id, "text", null, 20));
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            service.ReadThreadItemsAsync(thread.Id, "turn_001", ["item_001"]));
    }
}
