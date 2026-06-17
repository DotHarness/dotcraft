using DotCraft.Context;
using DotCraft.Protocol;
using DotCraft.Tracing;

namespace DotCraft.Tests.Protocol;

/// <summary>
/// Streaming providers often send cumulative <c>UsageContent</c> snapshots; <see cref="UsageSnapshotDelta"/>
/// converts them to true deltas for <c>item/usage/delta</c> (see appserver-protocol §6.6).
/// </summary>
public sealed class UsageSnapshotDeltaTests
{
    [Fact]
    public void RequestAccumulator_ExplicitRequestBoundaries_SumIncreasingRequestInputs()
    {
        var accumulator = new TokenUsageRequestAccumulator();
        long totalInput = 0;
        long totalCachedInput = 0;
        long latestContextInput = 0;
        var llmCalls = 0;

        void Step(int requestIndex, long input, long cachedInput)
        {
            var delta = accumulator.ApplySnapshot(
                new TokenUsageSnapshot(
                    InputTokens: input,
                    OutputTokens: 0,
                    CachedInputTokens: cachedInput,
                    ReasoningOutputTokens: 0),
                requestIndex);
            totalInput += delta.Usage.InputTokens;
            totalCachedInput += delta.Usage.CachedInputTokens;
            latestContextInput = input;
            llmCalls += delta.LlmCallDelta;
        }

        Step(1, 12_000, 8_000);
        Step(2, 20_000, 18_000);
        Step(3, 41_000, 40_000);

        Assert.Equal(73_000, totalInput);
        Assert.Equal(66_000, totalCachedInput);
        Assert.Equal(41_000, latestContextInput);
        Assert.Equal(3, llmCalls);
    }

    [Fact]
    public void RequestAccumulator_SameRequestCumulativeSnapshots_YieldFinalRequestTotal()
    {
        var accumulator = new TokenUsageRequestAccumulator();

        var first = accumulator.ApplySnapshot(
            new TokenUsageSnapshot(2_000, OutputTokens: 0, CachedInputTokens: 1_000, ReasoningOutputTokens: 0),
            requestIndex: 1);
        var second = accumulator.ApplySnapshot(
            new TokenUsageSnapshot(2_100, OutputTokens: 50, CachedInputTokens: 1_100, ReasoningOutputTokens: 0),
            requestIndex: 1);

        Assert.Equal(2_000, first.Usage.InputTokens);
        Assert.Equal(1, first.LlmCallDelta);
        Assert.Equal(100, second.Usage.InputTokens);
        Assert.Equal(100, second.Usage.CachedInputTokens);
        Assert.Equal(50, second.Usage.OutputTokens);
        Assert.Equal(0, second.LlmCallDelta);
        Assert.Equal(2_100, first.Usage.InputTokens + second.Usage.InputTokens);
        Assert.Equal(1_100, first.Usage.CachedInputTokens + second.Usage.CachedInputTokens);
    }

    [Fact]
    public void MonotonicSnapshots_2000_2100_2100_YieldDeltasSummingToFinalInput()
    {
        long lastIn = 0, lastOut = 0;
        long sumIn = 0, sumOut = 0;

        void Step(long curIn, long curOut)
        {
            UsageSnapshotDelta.Compute(curIn, curOut, ref lastIn, ref lastOut, out var dIn, out var dOut);
            sumIn += dIn;
            sumOut += dOut;
        }

        Step(2000, 0);
        Step(2100, 0);
        Step(2100, 50);

        Assert.Equal(2100, sumIn);
        Assert.Equal(50, sumOut);
        Assert.Equal(2100, lastIn);
        Assert.Equal(50, lastOut);
    }

    [Fact]
    public void NewSubRound_InputDecreases_FirstSnapshotTreatedAsDelta()
    {
        long lastIn = 0, lastOut = 0;

        UsageSnapshotDelta.Compute(5000, 200, ref lastIn, ref lastOut, out var d1In, out var d1Out);
        Assert.Equal(5000, d1In);
        Assert.Equal(200, d1Out);

        // New LLM HTTP call: cumulative resets (e.g. 3000 total for this request only)
        UsageSnapshotDelta.Compute(3000, 100, ref lastIn, ref lastOut, out var d2In, out var d2Out);
        Assert.Equal(3000, d2In);
        Assert.Equal(100, d2Out);
    }

    /// <summary>
    /// Invariant: sum of emitted deltas for the main agent stream matches the final cumulative snapshot
    /// (aligns with appserver-protocol §6.6 — client sum of <c>item/usage/delta</c> vs turn totals).
    /// </summary>
    [Fact]
    public void TokenTracker_UpdateWithStreamingDeltas_AccumulatesTotalsAndKeepsLastInputSnapshot()
    {
        var tracker = new TokenTracker();
        tracker.UpdateWithStreamingDeltas(2000, 0, 500, 100, 0, 2000);
        Assert.Equal(2000, tracker.TotalInputTokens);
        Assert.Equal(500, tracker.TotalCachedInputTokens);
        Assert.Equal(100, tracker.TotalCacheWriteInputTokens);
        Assert.Equal(2000, tracker.LastInputTokens);
        Assert.Equal(2000, tracker.LastContextTokens);

        tracker.UpdateWithStreamingDeltas(51000, 50, 40000, 0, 0, 51000, 50);
        Assert.Equal(53000, tracker.TotalInputTokens);
        Assert.Equal(40500, tracker.TotalCachedInputTokens);
        Assert.Equal(100, tracker.TotalCacheWriteInputTokens);
        Assert.Equal(50, tracker.TotalOutputTokens);
        Assert.Equal(51000, tracker.LastInputTokens);
        Assert.Equal(51050, tracker.LastContextTokens);

        tracker.AddSubAgentTokens(1000, 200, 900, 50, 0, llmCallCount: 2);
        Assert.Equal(1000, tracker.SubAgentInputTokens);
        Assert.Equal(900, tracker.SubAgentCachedInputTokens);
        Assert.Equal(50, tracker.SubAgentCacheWriteInputTokens);
        Assert.Equal(2, tracker.SubAgentLlmCallCount);
    }

    [Fact]
    public void TokenTracker_UpdateWithStreamingDeltas_RecordsProviderInputPlusOutputAsContext()
    {
        var tracker = new TokenTracker();

        tracker.UpdateWithStreamingDeltas(
            deltaInput: 212_505,
            deltaOutput: 2_751,
            deltaCachedInput: 0,
            deltaCacheWriteInput: 0,
            deltaReasoningOutput: 0,
            cumulativeInputSnapshot: 212_505,
            cumulativeOutputSnapshot: 2_751);

        Assert.Equal(212_505, tracker.LastInputTokens);
        Assert.Equal(215_256, tracker.LastContextTokens);
    }

    [Fact]
    public void RepeatedIdenticalSnapshots_YieldZeroAdditionalDelta()
    {
        long lastIn = 0, lastOut = 0;

        UsageSnapshotDelta.Compute(2100, 0, ref lastIn, ref lastOut, out var d1In, out var d1Out);
        Assert.Equal(2100, d1In);

        UsageSnapshotDelta.Compute(2100, 0, ref lastIn, ref lastOut, out var d2In, out var d2Out);
        Assert.Equal(0, d2In);
        Assert.Equal(0, d2Out);
    }
}
