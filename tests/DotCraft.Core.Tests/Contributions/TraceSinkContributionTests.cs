using System.Diagnostics;
using DotCraft.Contributions;
using DotCraft.Tracing;
using Xunit;
using TraceEventType = DotCraft.Tracing.TraceEventType;

namespace DotCraft.Tests.Contributions;

/// <summary>The additive trace sink contribution point: fan-out off the recording thread, containment of a throwing
/// sink, and the bounded hand-off that keeps a stalled sink out of the trace path.</summary>
public sealed class TraceSinkContributionTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void ContributedSinks_ObserveEveryRecordedEvent_InOrder()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        var first = new RecordingSink("first");
        var second = new RecordingSink("second");
        registry.Add<ITraceSink>(first, new ContributionOptions(Order: 100));
        registry.Add<ITraceSink>(second, new ContributionOptions(Order: 200));

        store.Record(NewEvent("a"));
        store.Record(NewEvent("b"));
        store.Record(NewEvent("c"));

        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        Assert.Equal(["a", "b", "c"], first.Ids);
        Assert.Equal(["a", "b", "c"], second.Ids);
    }

    [Fact]
    public void AThrowingSink_IsLoggedAndSkipped_AndTheRestStillRun()
    {
        var registry = new ContributionRegistry();
        var logs = new CapturingLoggerFactory();
        using var dispatcher = new TraceSinkDispatcher(registry, logs);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        var survivor = new RecordingSink("survivor");
        registry.Add<ITraceSink>(new ThrowingSink(), new ContributionOptions(Order: 100));
        registry.Add<ITraceSink>(survivor, new ContributionOptions(Order: 200));

        store.Record(NewEvent("a"));
        store.Record(NewEvent("b"));

        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        Assert.Equal(["a", "b"], survivor.Ids);
        Assert.Contains(logs.Warnings, message => message.Contains("throwing", StringComparison.Ordinal));
    }

    [Fact]
    public void AStalledSink_DoesNotStallRecording()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        using var gate = new ManualResetEventSlim(false);
        var stalled = new BlockingSink(gate);
        registry.Add<ITraceSink>(stalled, new ContributionOptions(Order: 100));

        store.Record(NewEvent("first"));
        Assert.True(stalled.Entered.Wait(DrainTimeout));

        var clock = Stopwatch.StartNew();
        for (var index = 0; index < 200; index++)
            store.Record(NewEvent($"e{index}"));
        clock.Stop();

        // The sink is still parked on the first event; recording never waited on it.
        Assert.Equal(1, stalled.Entries);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Recording took {clock.Elapsed}.");
        Assert.Equal(201, store.GetEvents("trace-sink-contribution").Count);

        gate.Set();
        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
    }

    [Fact]
    public void ALateRegisteredSink_ObservesTheNextEvent()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);

        store.Record(NewEvent("before"));
        var sink = new RecordingSink("late");
        registry.Add<ITraceSink>(sink);
        store.Record(NewEvent("after"));

        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        Assert.Equal(["after"], sink.Ids);
    }

    [Fact]
    public void AThreadScopedSink_IsNotInvoked()
    {
        // A trace event names a session key, never a thread id, so the contribution point resolves workspace scope only.
        var registry = new ContributionRegistry();
        using var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        var scoped = new RecordingSink("scoped");
        var workspace = new RecordingSink("workspace");
        registry.Add<ITraceSink>(scoped, ContributionOptions.ForThread("thread-1"));
        registry.Add<ITraceSink>(workspace);

        store.Record(NewEvent("a"));

        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        Assert.Equal(["a"], workspace.Ids);
        Assert.Empty(scoped.Ids);
    }

    [Fact]
    public void RevokingASink_StopsItsDelivery()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        var sink = new RecordingSink("revoked");
        var handle = registry.Add<ITraceSink>(sink);

        store.Record(NewEvent("kept"));
        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        handle.Dispose();
        store.Record(NewEvent("dropped"));

        Assert.True(dispatcher.WaitForPendingSinks(DrainTimeout));
        Assert.Equal(["kept"], sink.Ids);
    }

    [Fact]
    public void DisposingTheDispatcher_StopsPublishing()
    {
        var registry = new ContributionRegistry();
        var dispatcher = new TraceSinkDispatcher(registry);
        var store = new TraceStore(sinkDispatcher: dispatcher);
        var sink = new RecordingSink("after-dispose");
        registry.Add<ITraceSink>(sink);

        dispatcher.Dispose();
        dispatcher.Dispose();
        store.Record(NewEvent("a"));

        Assert.Empty(sink.Ids);
    }

    [Fact]
    public void AStoreWithoutADispatcher_RecordsNormally()
    {
        var store = new TraceStore();

        store.Record(NewEvent("a"));

        Assert.Single(store.GetEvents("trace-sink-contribution"));
    }

    private static TraceEvent NewEvent(string id) => new()
    {
        Id = id,
        Type = TraceEventType.Request,
        SessionKey = "trace-sink-contribution",
        Content = id
    };

    private sealed class RecordingSink(string name) : ITraceSink
    {
        private readonly List<string> _ids = [];

        public string Name => name;

        public IReadOnlyList<string> Ids
        {
            get
            {
                lock (_ids)
                    return _ids.ToArray();
            }
        }

        public void Record(TraceEvent evt)
        {
            lock (_ids)
                _ids.Add(evt.Id);
        }
    }

    private sealed class ThrowingSink : ITraceSink
    {
        public string Name => "throwing";

        public void Record(TraceEvent evt) => throw new InvalidOperationException("sink failure");
    }

    private sealed class BlockingSink(ManualResetEventSlim gate) : ITraceSink
    {
        private int _entries;

        public ManualResetEventSlim Entered { get; } = new(false);

        public int Entries => Volatile.Read(ref _entries);

        public string Name => "blocking";

        public void Record(TraceEvent evt)
        {
            Interlocked.Increment(ref _entries);
            Entered.Set();
            gate.Wait(TimeSpan.FromSeconds(30));
        }
    }
}
