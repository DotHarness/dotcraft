using System.Diagnostics;
using DotCraft.Contributions;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Contributions;

public sealed class ThreadRuntimeSignalContributionTests
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void ContributedObservers_SeeEverySignalInOrder_AndAThreadScopedOneOnlyItsThread()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new ThreadRuntimeSignalDispatcher(registry);
        var first = new RecordingContributor();
        var second = new RecordingContributor();
        registry.Add<IThreadRuntimeSignalContributor>(first, new ContributionOptions(Order: 100));
        registry.Add<IThreadRuntimeSignalContributor>(second, new ContributionOptions(Order: 200));
        var scoped = new RecordingContributor();
        registry.Add<IThreadRuntimeSignalContributor>(scoped, ContributionOptions.ForThread("thread-2"));

        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.ApprovalRequested);
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation);
        dispatcher.Publish("thread-2", SessionThreadRuntimeSignal.TurnCompleted);

        Assert.True(dispatcher.WaitForPendingSignals(DrainTimeout));
        var expected = new[]
        {
            SessionThreadRuntimeSignal.TurnStarted,
            SessionThreadRuntimeSignal.ApprovalRequested,
            SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
        };
        Assert.Equal(expected.Append(SessionThreadRuntimeSignal.TurnCompleted), first.Signals);
        Assert.Equal(expected.Append(SessionThreadRuntimeSignal.TurnCompleted), second.Signals);
        Assert.Equal(["thread-1", "thread-1", "thread-1", "thread-2"], first.Threads);
        Assert.Equal(["thread-2"], scoped.Threads);
    }

    [Fact]
    public void AThrowingContributor_IsLoggedAndSkipped_AndTheRestStillRun()
    {
        var registry = new ContributionRegistry();
        var logs = new CapturingLoggerFactory();
        using var dispatcher = new ThreadRuntimeSignalDispatcher(registry, logs);
        var survivor = new RecordingContributor();
        registry.Add<IThreadRuntimeSignalContributor>(new ThrowingContributor(), new ContributionOptions(Order: 100));
        registry.Add<IThreadRuntimeSignalContributor>(survivor, new ContributionOptions(Order: 200));

        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnCompleted);

        Assert.True(dispatcher.WaitForPendingSignals(DrainTimeout));
        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnCompleted],
            survivor.Signals);
        Assert.Contains(logs.Warnings, message => message.Contains("ThrowingContributor", StringComparison.Ordinal));
    }

    [Fact]
    public void AStalledContributor_DoesNotStallPublishing()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new ThreadRuntimeSignalDispatcher(registry);
        using var gate = new ManualResetEventSlim(false);
        var stalled = new BlockingContributor(gate);
        registry.Add<IThreadRuntimeSignalContributor>(stalled);

        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);
        Assert.True(stalled.Entered.Wait(DrainTimeout));

        var clock = Stopwatch.StartNew();
        for (var index = 0; index < 2000; index++)
            dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnCompleted);
        clock.Stop();

        Assert.Equal(1, stalled.Entries);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Publishing took {clock.Elapsed}.");

        gate.Set();
        Assert.True(dispatcher.WaitForPendingSignals(DrainTimeout));
        Assert.True(
            stalled.Entries <= ThreadRuntimeSignalDispatcher.QueueCapacity + 1,
            $"The queue grew past its bound: {stalled.Entries} delivered.");
    }

    [Fact]
    public void ALateRegisteredContributor_ObservesTheNextSignalUntilRevoked()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new ThreadRuntimeSignalDispatcher(registry);

        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);
        var contributor = new RecordingContributor();
        var handle = registry.Add<IThreadRuntimeSignalContributor>(contributor);
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnCompleted);
        Assert.True(dispatcher.WaitForPendingSignals(DrainTimeout));
        handle.Dispose();
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.ContextCompacted);

        Assert.True(dispatcher.WaitForPendingSignals(DrainTimeout));
        Assert.Equal([SessionThreadRuntimeSignal.TurnCompleted], contributor.Signals);
    }

    [Fact]
    public void DisposingTheDispatcher_StopsPublishing_AndCancelsInFlightWork()
    {
        var registry = new ContributionRegistry();
        var dispatcher = new ThreadRuntimeSignalDispatcher(registry);
        var contributor = new RecordingContributor();
        registry.Add<IThreadRuntimeSignalContributor>(contributor);
        var waiting = new CancellationObservingContributor();
        registry.Add<IThreadRuntimeSignalContributor>(waiting, new ContributionOptions(Order: 200));

        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);
        Assert.True(waiting.Entered.Wait(DrainTimeout));
        dispatcher.Dispose();
        dispatcher.Dispose();
        dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnCompleted);

        Assert.True(waiting.Cancelled.Wait(DrainTimeout));
        Assert.Equal([SessionThreadRuntimeSignal.TurnStarted], contributor.Signals);
    }

    [Fact]
    public void AnEmptyContributionPoint_QueuesNothing()
    {
        var registry = new ContributionRegistry();
        using var dispatcher = new ThreadRuntimeSignalDispatcher(registry);

        for (var index = 0; index < 10_000; index++)
            dispatcher.Publish("thread-1", SessionThreadRuntimeSignal.TurnStarted);

        Assert.True(dispatcher.WaitForPendingSignals(TimeSpan.FromMilliseconds(200)));
    }

    private sealed class RecordingContributor : IThreadRuntimeSignalContributor
    {
        private readonly List<ThreadRuntimeSignalContext> _seen = [];

        public IReadOnlyList<SessionThreadRuntimeSignal> Signals
        {
            get
            {
                lock (_seen)
                    return _seen.Select(entry => entry.Signal).ToArray();
            }
        }

        public IReadOnlyList<string> Threads
        {
            get
            {
                lock (_seen)
                    return _seen.Select(entry => entry.ThreadId).ToArray();
            }
        }

        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default)
        {
            lock (_seen)
                _seen.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingContributor : IThreadRuntimeSignalContributor
    {
        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("contributor failure");
    }

    private sealed class BlockingContributor(ManualResetEventSlim gate) : IThreadRuntimeSignalContributor
    {
        private int _entries;

        public ManualResetEventSlim Entered { get; } = new(false);

        public int Entries => Volatile.Read(ref _entries);

        public Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _entries);
            Entered.Set();
            gate.Wait(TimeSpan.FromSeconds(30));
            return Task.CompletedTask;
        }
    }

    private sealed class CancellationObservingContributor : IThreadRuntimeSignalContributor
    {
        public ManualResetEventSlim Entered { get; } = new(false);

        public ManualResetEventSlim Cancelled { get; } = new(false);

        public async Task OnThreadRuntimeSignalAsync(
            ThreadRuntimeSignalContext context,
            CancellationToken cancellationToken = default)
        {
            Entered.Set();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.Set();
            }
        }
    }
}
