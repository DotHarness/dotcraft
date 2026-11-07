using System.Text;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class ThreadObservationBrokerTests
{
    [Fact]
    public async Task Observation_JoinsThePublishedPrefixAndFutureEventsWithoutReplay()
    {
        var thread = Thread();
        var broker = new ThreadEventBroker(thread.Id);
        broker.Initialize(thread, 1);
        var channel = broker.CreateTurnChannel("turn_001");
        var turn = new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running };
        var item = new SessionItem
        {
            Id = "item_001", TurnId = turn.Id, Type = ItemType.AgentMessage,
            Status = ItemStatus.Streaming, Payload = new AgentMessagePayload()
        };
        turn.Items.Add(item);
        var prefixReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowTerminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishing = Task.Run(async () =>
        {
            channel.EmitTurnStarted(turn);
            channel.EmitItemStarted(item);
            for (var i = 0; i < 200; i++)
            {
                channel.EmitItemDelta(item, new AgentMessageDelta { TextDelta = $"{i}|" });
                if (i == 0) prefixReady.SetResult();
            }
            await allowTerminal.Task;
            turn.Status = TurnStatus.Completed;
            channel.EmitTurnCompleted(turn);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await prefixReady.Task.WaitAsync(timeout.Token);
        using var observation = broker.Observe(thread, null, ThreadObservationView.Full, [], timeout.Token);
        var prefix = Assert.Single(observation.Snapshot.Turn!.Items).AsAgentMessage!.Text;
        allowTerminal.SetResult();
        await publishing.WaitAsync(timeout.Token);

        var text = new StringBuilder(prefix);
        await foreach (var evt in observation.Events.WithCancellation(timeout.Token))
        {
            if (evt.DeltaPayload is { } delta) text.Append(delta.TextDelta);
            if (evt.EventType == SessionEventType.TurnCompleted) break;
        }
        Assert.Equal(string.Concat(Enumerable.Range(0, 200).Select(i => $"{i}|")), text.ToString());
        Assert.Equal(prefix, Assert.Single(observation.Snapshot.Turn.Items).AsAgentMessage!.Text);
        Assert.False(broker.HasSubscribers);
    }

    [Fact]
    public void Cancellation_ReleasesAnObservationBeforeItsFeedIsEnumerated()
    {
        var broker = new ThreadEventBroker("thread_001");
        using var cancellation = new CancellationTokenSource();
        using var observation = broker.Observe(Thread(), null, ThreadObservationView.Metadata, [], cancellation.Token);
        Assert.True(broker.HasSubscribers);
        cancellation.Cancel();
        Assert.False(broker.HasSubscribers);
    }

    [Fact]
    public async Task RuntimeRelease_ClosesItsFeedAndRejectsRetiredProducers()
    {
        var manager = new ThreadManager();
        var thread = Thread();
        var original = manager.SetThread(thread);
        var oldChannel = original.Broker.CreateTurnChannel("turn_001");
        oldChannel.EmitTurnStarted(new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running });
        using var originalView = original.Broker.Observe(thread, null, ThreadObservationView.Metadata, []);
        Assert.True(manager.TryRemove(original));
        await original.DisposeAsync();
        await using var oldFeed = originalView.Events.GetAsyncEnumerator();
        Assert.False(await oldFeed.MoveNextAsync());

        var replacementThread = Thread();
        replacementThread.Turns.Add(new SessionTurn { Id = "turn_002", ThreadId = thread.Id, Status = TurnStatus.Completed });
        var replacement = manager.SetThread(replacementThread);
        oldChannel.EmitTurnCompleted(new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Completed });
        replacement.Broker.ReleaseCurrentState(original.Generation);
        var lateChannel = replacement.Broker.CreateTurnChannel("turn_999", runtimeGeneration: original.Generation);
        lateChannel.EmitTurnStarted(new SessionTurn { Id = "turn_999", ThreadId = thread.Id, Status = TurnStatus.Running });
        using var currentView = replacement.Broker.Observe(replacementThread, null, ThreadObservationView.Metadata, []);
        Assert.Equal("turn_002", currentView.Snapshot.Turn!.Id);
        manager.ForgetBroker(thread.Id);
        Assert.False(replacement.Broker.HasSubscribers);
        Assert.Throws<ObjectDisposedException>(() => replacement.Broker.Observe(
            replacementThread, null, ThreadObservationView.Metadata, []));
        await replacement.DisposeAsync();
    }

    [Fact]
    public async Task Refresh_WritesItsSnapshotBetweenQueuedEventsWithoutHoldingPublication()
    {
        var thread = Thread();
        var broker = new ThreadEventBroker(thread.Id);
        broker.Initialize(thread, 1);
        var channel = broker.CreateTurnChannel("turn_001");
        var turn = new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Running };
        var item = new SessionItem
        {
            Id = "item_001", TurnId = turn.Id, Type = ItemType.AgentMessage,
            Status = ItemStatus.Streaming, Payload = new AgentMessagePayload()
        };
        channel.EmitTurnStarted(turn);
        channel.EmitItemStarted(item);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var observation = broker.Observe(thread, null, ThreadObservationView.Metadata, [], timeout.Token);
        channel.EmitItemDelta(item, new AgentMessageDelta { TextDelta = "before" });
        var responseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var refresh = observation.RefreshAsync(ThreadObservationView.Full, [], async snapshot =>
        {
            order.Add("response:" + Assert.Single(snapshot.Turn!.Items).AsAgentMessage!.Text);
            responseEntered.SetResult();
            await releaseResponse.Task.WaitAsync(timeout.Token);
        }, timeout.Token);
        channel.EmitItemDelta(item, new AgentMessageDelta { TextDelta = "after" });
        var pump = Task.Run(async () =>
        {
            await foreach (var evt in observation.Events.WithCancellation(timeout.Token))
            {
                if (evt.DeltaPayload is { } delta) order.Add(delta.TextDelta);
                if (evt.EventType == SessionEventType.TurnCompleted) break;
            }
        });
        await responseEntered.Task.WaitAsync(timeout.Token);
        Assert.Equal(["before", "response:before"], order);
        turn.Status = TurnStatus.Completed;
        channel.EmitTurnCompleted(turn);
        releaseResponse.SetResult();
        await refresh;
        await pump.WaitAsync(timeout.Token);
        Assert.Equal(["before", "response:before", "after"], order);
        Assert.False(broker.HasSubscribers);
    }

    [Fact]
    public async Task Release_CancelsAQueuedRefreshAndRemovesItsSubscriber()
    {
        var thread = Thread();
        var broker = new ThreadEventBroker(thread.Id);
        broker.Initialize(thread, 1);
        var observation = broker.Observe(thread, null, ThreadObservationView.Metadata, []);
        var refresh = observation.RefreshAsync(ThreadObservationView.Full, [], _ => Task.CompletedTask);
        observation.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(broker.HasSubscribers);
    }

    [Fact]
    public async Task PlanSave_UpdatesOnlyTheResidentViewAndSnapshotsRemainFrozen()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            using var database = new DotCraft.Persistence.WorkspaceStateDatabase(directory);
            var plans = new DotCraft.Memory.PlanStore(directory, database);
            using var manager = new ThreadManager(plans);
            var thread = Thread();
            await new ThreadStore(directory, database).SaveThreadAsync(thread);
            var runtime = manager.SetThread(thread);
            var plan = new DotCraft.Memory.StructuredPlan
            {
                Title = "first", Todos = [new() { Id = "one", Status = "pending" }]
            };
            await plans.SaveStructuredPlanAsync(thread.Id, plan);
            using var first = runtime.Broker.Observe(thread, null, ThreadObservationView.Full, []);
            plan.Title = "mutated";
            first.Snapshot.Plan!.Todos.Clear();
            using var unchanged = runtime.Broker.Observe(thread, null, ThreadObservationView.Metadata, []);
            Assert.Equal("first", unchanged.Snapshot.Plan!.Title);
            Assert.Single(unchanged.Snapshot.Plan.Todos);
            await plans.SaveStructuredPlanAsync(thread.Id, new DotCraft.Memory.StructuredPlan { Title = "second" });
            using var next = runtime.Broker.Observe(thread, null, ThreadObservationView.Metadata, []);
            Assert.Equal("first", first.Snapshot.Plan!.Title);
            Assert.Equal("second", next.Snapshot.Plan!.Title);
            await runtime.DisposeAsync();
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Rollback_InvalidatesAQueuedSnapshotBeforeItsResponseRuns()
    {
        var thread = Thread();
        var broker = new ThreadEventBroker(thread.Id);
        broker.Initialize(thread, 1);
        using var observation = broker.Observe(thread, null, ThreadObservationView.Metadata, []);
        var responded = false;
        var response = observation.RefreshAsync(ThreadObservationView.Full, [], _ =>
        {
            responded = true;
            return Task.CompletedTask;
        });
        broker.ResetCurrentState(thread, 1);
        using var stop = new CancellationTokenSource();
        var pump = Task.Run(async () =>
        {
            await foreach (var _ in observation.Events.WithCancellation(stop.Token)) { }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
        Assert.False(responded);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);
    }

    private static SessionThread Thread() => new()
    {
        Id = "thread_001", Status = ThreadStatus.Active,
        Configuration = new ThreadConfiguration { Mode = "agent" }
    };
}
