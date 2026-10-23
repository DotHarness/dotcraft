using DotCraft.Agents;
using DotCraft.Sessions;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentProgressAggregatorTests : IAsyncLifetime
{
    private const string ThreadId = "thread_test";
    private const string TurnId = "turn_test";

    private readonly List<SubAgentProgressPayload> _capturedPayloads = [];
    private readonly SessionEventChannel _channel;

    public SubAgentProgressAggregatorTests()
    {
        _channel = new SessionEventChannel(
            ThreadId, TurnId,
            publish: evt =>
            {
                if (evt.EventType == SessionEventType.SubAgentProgress
                    && evt.SubAgentProgressPayload is { } payload)
                {
                    lock (_capturedPayloads)
                        _capturedPayloads.Add(payload);
                }
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SubAgentProgressBridge.Remove("agent-A");
        SubAgentProgressBridge.Remove("agent-B");
        SubAgentProgressBridge.Remove("agent-C");
        _channel.Complete();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Aggregator_EmitsPeriodicSnapshots_AtConfiguredInterval()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.AddTokens(100, 50);

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        await WaitForSnapshotCountAsync(2);

        entry.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => item.IsCompleted));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        Assert.True(snapshots.Count >= 2,
            $"Expected at least 2 periodic snapshots, got {snapshots.Count}");

        foreach (var snapshot in snapshots)
        {
            var e = Assert.Single(snapshot.Entries);
            Assert.Equal("agent-A", e.Label);
            Assert.Equal(100, e.InputTokens);
            Assert.Equal(50, e.OutputTokens);
        }
    }

    [Fact]
    public async Task Aggregator_EmitsSnapshotWithTokens_BeforeAllCompletedBreak()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        // Observe the initial poll before publishing the final token values.
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => !item.IsCompleted));

        entry.AddTokens(500, 200);
        entry.IsCompleted = true;

        await WaitForSnapshotAsync(snapshot => snapshot.Entries.Any(item =>
            item.IsCompleted && item.InputTokens == 500 && item.OutputTokens == 200));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        var completedSnapshot = snapshots.Find(s =>
            s.Entries.Any(e => e.IsCompleted && e.InputTokens == 500 && e.OutputTokens == 200));

        Assert.NotNull(completedSnapshot);
    }

    [Fact]
    public async Task Aggregator_DisposeAsync_EmitsFinalSnapshotAfterNaturalExit()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.AddTokens(100, 50);
        entry.IsCompleted = true;

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(30));
        aggregator.TrackLabel("agent-A");

        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => item.IsCompleted));

        int countBeforeDispose;
        lock (_capturedPayloads)
            countBeforeDispose = _capturedPayloads.Count;

        await aggregator.DisposeAsync();

        int countAfterDispose;
        lock (_capturedPayloads)
            countAfterDispose = _capturedPayloads.Count;

        Assert.True(countAfterDispose > countBeforeDispose,
            $"DisposeAsync should emit a final snapshot. Before: {countBeforeDispose}, After: {countAfterDispose}");

        SubAgentProgressPayload lastSnapshot;
        lock (_capturedPayloads)
            lastSnapshot = _capturedPayloads[^1];

        var finalEntry = Assert.Single(lastSnapshot.Entries);
        Assert.True(finalEntry.IsCompleted);
        Assert.Equal(100, finalEntry.InputTokens);
        Assert.Equal(50, finalEntry.OutputTokens);
    }

    [Fact]
    public async Task Aggregator_MultipleSubAgents_LastOneTokensInFinalSnapshot()
    {
        var entryA = SubAgentProgressBridge.GetOrCreate("agent-A");
        var entryB = SubAgentProgressBridge.GetOrCreate("agent-B");

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");
        aggregator.TrackLabel("agent-B");

        entryA.AddTokens(100, 50);
        entryA.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.Any(item =>
            item.Label == "agent-A" && item.IsCompleted));

        entryB.AddTokens(300, 150);
        entryB.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => item.IsCompleted));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        var finalSnapshot = snapshots.FindLast(s =>
            s.Entries.All(e => e.IsCompleted));
        Assert.NotNull(finalSnapshot);

        var a = finalSnapshot.Entries.First(e => e.Label == "agent-A");
        var b = finalSnapshot.Entries.First(e => e.Label == "agent-B");
        Assert.Equal(100, a.InputTokens);
        Assert.Equal(50, a.OutputTokens);
        Assert.Equal(300, b.InputTokens);
        Assert.Equal(150, b.OutputTokens);
    }

    [Fact]
    public async Task Aggregator_CancellationViaDispose_StopsGracefully()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        await WaitForSnapshotCountAsync(1);

        var disposeTask = aggregator.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(5000));
        Assert.Same(disposeTask, completed);
    }

    [Fact]
    public async Task Aggregator_TrackLabel_AutoStartsLoop()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.AddTokens(42, 21);

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(30));

        aggregator.TrackLabel("agent-A");
        await WaitForSnapshotCountAsync(1);

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        Assert.NotEmpty(snapshots);
        var e = Assert.Single(snapshots[0].Entries);
        Assert.Equal(42, e.InputTokens);
    }

    [Fact]
    public async Task Aggregator_TokensAccumulateAcrossMultipleUpdates()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(40));
        aggregator.TrackLabel("agent-A");

        entry.AddTokens(100, 50);

        entry.AddTokens(200, 100);

        entry.AddTokens(300, 150);
        entry.IsCompleted = true;

        // Force a final snapshot instead of relying on a timer tick under test load.
        await aggregator.DisposeAsync();

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        var lastSnapshot = snapshots[^1];
        var e = Assert.Single(lastSnapshot.Entries);
        Assert.Equal(600, e.InputTokens);
        Assert.Equal(300, e.OutputTokens);
        Assert.True(e.IsCompleted);
    }

    [Fact]
    public async Task Aggregator_CurrentTool_CapturedInSnapshot()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.CurrentTool = "ReadFile";

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(30));
        aggregator.TrackLabel("agent-A");
        await aggregator.DisposeAsync();

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        Assert.NotEmpty(snapshots);
        var e = Assert.Single(snapshots[^1].Entries);
        Assert.Equal("ReadFile", e.CurrentTool);
    }

    private async Task WaitForSnapshotCountAsync(int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (_capturedPayloads)
            {
                if (_capturedPayloads.Count >= count)
                    return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private async Task WaitForSnapshotAsync(Func<SubAgentProgressPayload, bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            lock (_capturedPayloads)
            {
                if (_capturedPayloads.Any(predicate))
                    return;
            }

            await Task.Delay(10, timeout.Token);
        }
    }
}
