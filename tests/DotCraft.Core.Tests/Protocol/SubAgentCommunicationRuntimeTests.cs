using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentCommunicationRuntimeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("message")]
    [InlineData("UNKNOWN")]
    public void CommunicationEnvelope_RejectsUndefinedMessageTypes(string messageType)
    {
        var communication = new SubAgentCommunication
        {
            Id = "communication-1",
            RootThreadId = "root-a",
            AuthorAgentPath = AgentPath.Root,
            RecipientAgentPath = "/root/worker",
            MessageType = messageType,
            Payload = "payload"
        };

        Assert.Throws<InvalidOperationException>(() => communication.RenderForModel());
    }

    [Fact]
    public async Task Activity_IsPartitionedByRootAndTargetWhileGraphWakesTheRootTree()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var rootAWorker = runtime.Subscribe("root-a", "/root/worker", out var rootAWorkerActivity);
        using var rootARoot = runtime.Subscribe("root-a", AgentPath.Root, out var rootARootActivity);
        using var rootBWorker = runtime.Subscribe("root-b", "/root/worker", out var rootBWorkerActivity);

        runtime.PublishMailbox("root-a", "/root/worker");

        await rootAWorkerActivity.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(rootARootActivity.IsCompleted);
        Assert.False(rootBWorkerActivity.IsCompleted);

        using var rootAWorkerGraph = runtime.Subscribe("root-a", "/root/worker", out var rootAWorkerGraphActivity);
        using var rootARootGraph = runtime.Subscribe("root-a", AgentPath.Root, out var rootARootGraphActivity);
        using var rootBWorkerGraph = runtime.Subscribe("root-b", "/root/worker", out var rootBWorkerGraphActivity);

        runtime.PublishGraph("root-a");

        await Task.WhenAll(rootAWorkerGraphActivity, rootARootGraphActivity).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(rootBWorkerGraphActivity.IsCompleted);
    }

    [Fact]
    public async Task Steer_WakesOnlyTheTargetPath()
    {
        var runtime = new SubAgentCommunicationRuntime();
        using var target = runtime.Subscribe("root-a", "/root/worker", out var targetActivity);
        using var peer = runtime.Subscribe("root-a", "/root/reviewer", out var peerActivity);

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
