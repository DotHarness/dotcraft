using DotCraft.RemoteTools;
using DotCraft.Satellite.ViewModels;
using Xunit;

namespace DotCraft.Satellite.Tests;

public sealed class IslandApprovalQueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 11, 35, 0, TimeSpan.Zero);

    [Fact]
    public async Task Answer_DecidesTheRequestTheOwnerIsLookingAtAndMovesOn()
    {
        var queue = new IslandApprovalQueue();
        var first = Entry("first");
        var second = Entry("second");
        queue.Add(first);
        queue.Add(second);

        queue.Answer(allowed: true);

        Assert.True(first.Decision.IsCompleted);
        Assert.True(await first.Decision);
        Assert.False(second.Decision.IsCompleted);
        Assert.Same(second, queue.Head);
        Assert.Single(queue.Pending);
    }

    [Fact]
    public async Task Expire_DeniesARequestTwoMinutesAfterItArrived()
    {
        var queue = new IslandApprovalQueue();
        var stale = Entry("stale", age: IslandApprovalQueue.Window);
        var fresh = Entry("fresh", age: TimeSpan.FromSeconds(119));
        queue.Add(stale);
        queue.Add(fresh);

        queue.Expire(Now);

        Assert.True(stale.Decision.IsCompleted);
        Assert.False(await stale.Decision);
        Assert.False(fresh.Decision.IsCompleted);
        Assert.Same(fresh, queue.Head);
    }

    [Fact]
    public async Task Invalidate_DeniesOnlyTheRequestsOfTheMachineThatWentAway()
    {
        var queue = new IslandApprovalQueue();
        var ann = Entry("ann", peerId: "pair_ann");
        var priya = Entry("priya", peerId: "pair_priya");
        queue.Add(ann);
        queue.Add(priya);

        queue.Invalidate("pair_ann");

        Assert.True(ann.Decision.IsCompleted);
        Assert.False(await ann.Decision);
        Assert.Same(priya, queue.Head);
    }

    [Fact]
    public async Task Invalidate_WithoutAMachine_DeniesEverythingWaiting()
    {
        var queue = new IslandApprovalQueue();
        var first = Entry("first");
        var second = Entry("second", peerId: "pair_priya");
        queue.Add(first);
        queue.Add(second);

        queue.Invalidate();

        Assert.Empty(queue.Pending);
        Assert.False(await first.Decision);
        Assert.False(await second.Decision);
    }

    [Fact]
    public async Task Disable_DeniesWhatIsWaitingAndWhatArrivesAfterward()
    {
        var queue = new IslandApprovalQueue();
        var waiting = Entry("waiting");
        queue.Add(waiting);

        queue.Disable();
        var later = Entry("later");
        queue.Add(later);

        Assert.Empty(queue.Pending);
        Assert.False(await waiting.Decision);
        Assert.False(await later.Decision);
    }

    [Fact]
    public async Task Remove_TreatsARequestNobodyAnsweredAsADenial()
    {
        var queue = new IslandApprovalQueue();
        var entry = Entry("first");
        queue.Add(entry);

        queue.Remove(entry);

        Assert.Empty(queue.Pending);
        Assert.False(await entry.Decision);
    }

    private static IslandApprovalEntry Entry(
        string target,
        string peerId = "pair_ann",
        TimeSpan? age = null) => new(
        new RemoteToolApprovalRequest(
            peerId, "Ann", 1, target, "shell", "execute", target, Path.GetTempPath()),
        Now - (age ?? TimeSpan.Zero));
}
