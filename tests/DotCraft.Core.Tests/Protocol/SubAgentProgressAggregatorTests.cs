using DotCraft.Agents;
using DotCraft.Protocol;
using DotCraft.Sessions;
using Xunit;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for <see cref="SubAgentProgressAggregator"/>.
/// Validates:
/// - Periodic snapshot emission with correct token data
/// - AllCompleted() natural exit emits a final snapshot before breaking
/// - DisposeAsync() emits an additional final snapshot after loop exit
/// - Empty tracker emits nothing
/// </summary>
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
        // Clean up any bridge entries left over from tests
        SubAgentProgressBridge.Remove("agent-A");
        SubAgentProgressBridge.Remove("agent-B");
        SubAgentProgressBridge.Remove("agent-C");
        _channel.Complete();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Test 1: Aggregator emits snapshots at the configured interval
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_EmitsPeriodicSnapshots_AtConfiguredInterval()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.AddTokens(100, 50);

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        await WaitForSnapshotCountAsync(2);

        // Mark completed so the loop exits
        entry.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => item.IsCompleted));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        Assert.True(snapshots.Count >= 2,
            $"Expected at least 2 periodic snapshots, got {snapshots.Count}");

        // All snapshots should have the correct token data
        foreach (var snapshot in snapshots)
        {
            var e = Assert.Single(snapshot.Entries);
            Assert.Equal("agent-A", e.Label);
            Assert.Equal(100, e.InputTokens);
            Assert.Equal(50, e.OutputTokens);
        }
    }

    // -------------------------------------------------------------------------
    // Test 2: Snapshot before AllCompleted() break includes final tokens
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_EmitsSnapshotWithTokens_BeforeAllCompletedBreak()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        // Observe the initial poll before publishing the final token values.
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => !item.IsCompleted));

        // Now write tokens and mark completed BEFORE next poll
        entry.AddTokens(500, 200);
        entry.IsCompleted = true;

        await WaitForSnapshotAsync(snapshot => snapshot.Entries.Any(item =>
            item.IsCompleted && item.InputTokens == 500 && item.OutputTokens == 200));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        // At least one snapshot should have both IsCompleted=true and correct tokens
        var completedSnapshot = snapshots.Find(s =>
            s.Entries.Any(e => e.IsCompleted && e.InputTokens == 500 && e.OutputTokens == 200));

        Assert.NotNull(completedSnapshot);
    }

    // -------------------------------------------------------------------------
    // Test 3: DisposeAsync emits a final snapshot even after natural exit
    // -------------------------------------------------------------------------

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

        // DisposeAsync should emit an additional final snapshot
        await aggregator.DisposeAsync();

        int countAfterDispose;
        lock (_capturedPayloads)
            countAfterDispose = _capturedPayloads.Count;

        Assert.True(countAfterDispose > countBeforeDispose,
            $"DisposeAsync should emit a final snapshot. Before: {countBeforeDispose}, After: {countAfterDispose}");

        // The final snapshot must have the completed state with tokens
        SubAgentProgressPayload lastSnapshot;
        lock (_capturedPayloads)
            lastSnapshot = _capturedPayloads[^1];

        var finalEntry = Assert.Single(lastSnapshot.Entries);
        Assert.True(finalEntry.IsCompleted);
        Assert.Equal(100, finalEntry.InputTokens);
        Assert.Equal(50, finalEntry.OutputTokens);
    }

    // -------------------------------------------------------------------------
    // Test 4: Multiple SubAgents — last one's tokens visible in final snapshot
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_MultipleSubAgents_LastOneTokensInFinalSnapshot()
    {
        var entryA = SubAgentProgressBridge.GetOrCreate("agent-A");
        var entryB = SubAgentProgressBridge.GetOrCreate("agent-B");

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");
        aggregator.TrackLabel("agent-B");

        // Agent A completes first
        entryA.AddTokens(100, 50);
        entryA.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.Any(item =>
            item.Label == "agent-A" && item.IsCompleted));

        // Agent B completes later
        entryB.AddTokens(300, 150);
        entryB.IsCompleted = true;
        await WaitForSnapshotAsync(snapshot => snapshot.Entries.All(item => item.IsCompleted));

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        // There must be a snapshot where BOTH agents are completed with correct tokens
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

    // -------------------------------------------------------------------------
    // Test 5: Cancellation via CTS stops the loop gracefully
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_CancellationViaDispose_StopsGracefully()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        // NOT completed — the loop will not exit naturally

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(50));
        aggregator.TrackLabel("agent-A");

        await WaitForSnapshotCountAsync(1);

        // Dispose should cancel the loop and emit a final snapshot without hanging
        var disposeTask = aggregator.DisposeAsync().AsTask();
        var completed = await Task.WhenAny(disposeTask, Task.Delay(5000));
        Assert.Same(disposeTask, completed); // Should complete without timeout
    }

    // -------------------------------------------------------------------------
    // Test 6: TrackLabel auto-starts the aggregator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_TrackLabel_AutoStartsLoop()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");
        entry.AddTokens(42, 21);

        await using var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(30));

        // TrackLabel should auto-start the loop
        aggregator.TrackLabel("agent-A");
        await WaitForSnapshotCountAsync(1);

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        Assert.NotEmpty(snapshots);
        var e = Assert.Single(snapshots[0].Entries);
        Assert.Equal(42, e.InputTokens);
    }

    // -------------------------------------------------------------------------
    // Test 7: Token accumulation across multiple LLM calls
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Aggregator_TokensAccumulateAcrossMultipleUpdates()
    {
        var entry = SubAgentProgressBridge.GetOrCreate("agent-A");

        var aggregator = new SubAgentProgressAggregator(
            _channel, ThreadId, TurnId, interval: TimeSpan.FromMilliseconds(40));
        aggregator.TrackLabel("agent-A");

        // First LLM call
        entry.AddTokens(100, 50);

        // Second LLM call
        entry.AddTokens(200, 100);

        // Third LLM call + complete
        entry.AddTokens(300, 150);
        entry.IsCompleted = true;

        // Force a final snapshot instead of relying on a timer tick under test load.
        await aggregator.DisposeAsync();

        List<SubAgentProgressPayload> snapshots;
        lock (_capturedPayloads)
            snapshots = [.. _capturedPayloads];

        // The last snapshot should show cumulative tokens: 600 input, 300 output
        var lastSnapshot = snapshots[^1];
        var e = Assert.Single(lastSnapshot.Entries);
        Assert.Equal(600, e.InputTokens);
        Assert.Equal(300, e.OutputTokens);
        Assert.True(e.IsCompleted);
    }

    // -------------------------------------------------------------------------
    // Test 8: CurrentTool is captured in snapshot
    // -------------------------------------------------------------------------

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
