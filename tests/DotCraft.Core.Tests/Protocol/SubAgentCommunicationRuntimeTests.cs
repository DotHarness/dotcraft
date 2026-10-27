using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentCommunicationRuntimeTests
{
    [Fact]
    public async Task InputSubscription_WakesForMailboxOrSteer()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var mailbox = runtime.SubscribeInput("root-a", AgentPath.Root, out var mailboxActivity);
        runtime.PublishMailbox("root-a", AgentPath.Root);
        await mailboxActivity.WaitAsync(TimeSpan.FromSeconds(1));

        using var steer = runtime.SubscribeInput("root-a", AgentPath.Root, out var steerActivity);
        runtime.PublishSteer("root-a", AgentPath.Root);
        await steerActivity.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Activity_IsPartitionedByRootAndTarget()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var rootAWorker = runtime.SubscribeInput("root-a", "/root/worker", out var rootAWorkerActivity);
        using var rootARoot = runtime.SubscribeInput("root-a", AgentPath.Root, out var rootARootActivity);
        using var rootBWorker = runtime.SubscribeInput("root-b", "/root/worker", out var rootBWorkerActivity);

        runtime.PublishMailbox("root-a", "/root/worker");

        await rootAWorkerActivity.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(rootARootActivity.IsCompleted);
        Assert.False(rootBWorkerActivity.IsCompleted);
    }

    [Fact]
    public async Task Steer_WakesOnlyTheTargetPath()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var target = runtime.SubscribeInput("root-a", "/root/worker", out var targetActivity);
        using var peer = runtime.SubscribeInput("root-a", "/root/reviewer", out var peerActivity);

        runtime.PublishSteer("root-a", "/root/worker");

        await targetActivity.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(peerActivity.IsCompleted);
    }

    [Fact]
    public async Task InboxLease_SerializesConsumersForTheSameTargetOnly()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var first = await runtime.AcquireInboxAsync("root-a", "/root/worker", CancellationToken.None);
        var sameTarget = runtime.AcquireInboxAsync("root-a", "/root/worker", CancellationToken.None);
        var otherTarget = runtime.AcquireInboxAsync("root-a", "/root/reviewer", CancellationToken.None);

        using var peerLease = await otherTarget.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(sameTarget.IsCompleted);

        first.Dispose();
        using var second = await sameTarget.WaitAsync(TimeSpan.FromSeconds(1));
    }
}
