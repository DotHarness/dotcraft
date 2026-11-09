using DotCraft.Agents;
using DotCraft.Sessions;
using DotCraft.Tests.Contributions;
using Microsoft.Extensions.AI;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionTurnOutcomeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_IsPublishedAfterCommitOrReportedAsPersistenceError(bool failCommit)
    {
        using var host = new ContributionAgentHost("TurnFailureCommit");
        var client = new FailingContributionChatClient();
        var factory = host.CreateFactory(null, client);
        var writer = new GatedFailureWriter(failCommit);
        await using var store = new ThreadStore(host.WorkspacePath, null, null, writer);
        var service = new SessionService(factory, client.AsAIAgent(), new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = host.WorkspacePath
        });
        var failedSignals = 0;
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnFailed) Interlocked.Increment(ref failedSignals);
        };
        var events = new List<SessionEvent>();
        var run = CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")]), events);
        try
        {
            await writer.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, Volatile.Read(ref failedSignals));
            writer.Release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, failedSignals);
            var failed = Assert.Single(events, ev => ev.EventType == SessionEventType.TurnFailed).TurnPayload!;
            Assert.Equal(TurnStatus.Failed, failed.Status);
            if (failCommit)
            {
                Assert.Contains(failed.Items, item => item.AsError?.Code == "persistence_error");
                Assert.Contains("failed terminal commit", failed.Error);
            }
            else
            {
                var saved = await store.LoadThreadAsync(thread.Id);
                Assert.Equal(TurnStatus.Failed, Assert.Single(saved!.Turns).Status);
                Assert.DoesNotContain(failed.Items, item => item.AsError?.Code == "persistence_error");
            }
        }
        finally
        {
            writer.Release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task CompletedTurn_PostTerminalCallbackFailureDoesNotReplaceTheResult()
    {
        using var host = new ContributionAgentHost("TurnCompletedFollowup");
        var client = new ContributionChatClient("done");
        var factory = host.CreateFactory(null, client);
        await using var store = new ThreadStore(host.WorkspacePath);
        var service = new SessionService(factory, client.AsAIAgent(), new SessionPersistenceService(store), new SessionGate());
        var thread = await service.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "test", UserId = "user", WorkspacePath = host.WorkspacePath
        });
        service.ThreadRuntimeSignalForBroadcast = (_, signal, _) =>
        {
            if (signal == SessionThreadRuntimeSignal.TurnCompleted)
                throw new InvalidOperationException("post-terminal callback failed");
        };
        var events = new List<SessionEvent>();
        await CollectAsync(service.SubmitInputAsync(thread.Id, [new TextContent("work")]), events)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(events, ev => ev.EventType == SessionEventType.TurnCompleted);
        Assert.DoesNotContain(events, ev => ev.EventType == SessionEventType.TurnFailed);
        var saved = await store.LoadThreadAsync(thread.Id);
        Assert.Equal(TurnStatus.Completed, Assert.Single(saved!.Turns).Status);
        Assert.DoesNotContain(saved.Turns[0].Items, item => item.Type == ItemType.Error);
    }

    private static async Task CollectAsync(IAsyncEnumerable<SessionEvent> source, List<SessionEvent> events)
    {
        await foreach (var ev in source) events.Add(ev);
    }

    private sealed class GatedFailureWriter(bool failCommit) : IRolloutWriter
    {
        private readonly OrderedRolloutWriter _inner = new();
        private bool _intercepted;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task AddBatchAsync(string threadId, string path, IReadOnlyList<ThreadRolloutRecord> records,
            CancellationToken ct = default)
        {
            if (!_intercepted && records.Any(record => record.TurnCompleted?.Status == TurnStatus.Failed
                || record.TurnStateReplaced?.Turn.Status == TurnStatus.Failed))
            {
                _intercepted = true;
                Entered.TrySetResult();
                await Release.Task.WaitAsync(ct);
                if (failCommit)
                    throw new RolloutPersistenceException("failed terminal commit", new IOException("test write failure"));
            }
            await _inner.AddBatchAsync(threadId, path, records, ct);
        }

        public Task<RolloutWriteReceipt> FlushAsync(string threadId, CancellationToken ct = default) => _inner.FlushAsync(threadId, ct);
        public Task CloseAsync(string threadId, CancellationToken ct = default) => _inner.CloseAsync(threadId, ct);
        public Task ShutdownAllAsync(CancellationToken ct = default) => _inner.ShutdownAllAsync(ct);
    }
}
