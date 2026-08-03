using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Conformance tests for the <c>usage/timeseries</c> method (spec Section 27A.3),
/// which is gated behind the same <c>usageTelemetry</c> capability as <c>usage/summary</c>.
/// </summary>
public sealed class AppServerUsageTimeseriesTests
{
    [Fact]
    public async Task UsageTimeseries_ReturnsMethodNotFound_WhenTracingDisabled()
    {
        using var h = new AppServerTestHarness();
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task UsageTimeseries_BucketsTokensByDay_SparseAndAscending()
    {
        var traceStore = new TraceStore();
        // Two sessions on 2026-05-29 (UTC), one on 2026-05-31 (UTC). Nothing on the 30th.
        RecordSession(traceStore, "a", "2026-05-29T08:00:00Z", input: 100, output: 20);
        RecordSession(traceStore, "b", "2026-05-29T18:00:00Z", input: 40, output: 8);
        RecordSession(traceStore, "c", "2026-05-31T10:00:00Z", input: 10, output: 2);

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { tzOffsetMinutes = 0 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("tzOffsetMinutes").GetInt32());

        var days = result.GetProperty("days");
        // Sparse: only the two active days, ascending.
        Assert.Equal(2, days.GetArrayLength());

        var d0 = days[0];
        Assert.Equal("2026-05-29", d0.GetProperty("date").GetString());
        Assert.Equal(140, d0.GetProperty("inputTokens").GetInt64());
        Assert.Equal(28, d0.GetProperty("outputTokens").GetInt64());
        Assert.Equal(168, d0.GetProperty("totalTokens").GetInt64());
        Assert.Equal(2, d0.GetProperty("sessionCount").GetInt32());

        var d1 = days[1];
        Assert.Equal("2026-05-31", d1.GetProperty("date").GetString());
        Assert.Equal(12, d1.GetProperty("totalTokens").GetInt64());
        Assert.Equal(1, d1.GetProperty("sessionCount").GetInt32());
    }

    [Fact]
    public async Task UsageTimeseries_FiltersByInclusiveDateRange()
    {
        var traceStore = new TraceStore();
        RecordSession(traceStore, "a", "2026-05-28T12:00:00Z", input: 5, output: 1);
        RecordSession(traceStore, "b", "2026-05-29T12:00:00Z", input: 5, output: 1);
        RecordSession(traceStore, "c", "2026-05-30T12:00:00Z", input: 5, output: 1);

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries,
            new { from = "2026-05-29", to = "2026-05-29", tzOffsetMinutes = 0 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var days = resp.RootElement.GetProperty("result").GetProperty("days");
        Assert.Equal(1, days.GetArrayLength());
        Assert.Equal("2026-05-29", days[0].GetProperty("date").GetString());
    }

    [Fact]
    public async Task UsageTimeseries_TzOffsetShiftsBucketDayAcrossUtcMidnight()
    {
        var traceStore = new TraceStore();
        // 23:30 UTC on the 30th is 00:30 local on the 31st at UTC+1.
        RecordSession(traceStore, "a", "2026-05-30T23:30:00Z", input: 7, output: 3);

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { tzOffsetMinutes = 60 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(60, result.GetProperty("tzOffsetMinutes").GetInt32());
        var days = result.GetProperty("days");
        Assert.Equal(1, days.GetArrayLength());
        Assert.Equal("2026-05-31", days[0].GetProperty("date").GetString());
    }

    [Fact]
    public async Task UsageTimeseries_ReturnsInvalidParams_ForMalformedDate()
    {
        var traceStore = new TraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { from = "2026/05/29" });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task UsageTimeseries_ReturnsEmptyDays_WhenNoSessions()
    {
        var traceStore = new TraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("days").GetArrayLength());
        Assert.Equal(0, result.GetProperty("longestTaskMs").GetInt64());
    }

    [Fact]
    public async Task UsageTimeseries_ReportsLongestTaskAcrossSessions()
    {
        var traceStore = new TraceStore();
        RecordSession(traceStore, "a", "2026-05-29T08:00:00Z", input: 100, output: 20);
        RecordSession(traceStore, "b", "2026-05-30T08:00:00Z", input: 40, output: 8);
        // Turn durations: session a peaks at 5000ms, session b at 1000ms.
        RecordTurn(traceStore, "a", 1500);
        RecordTurn(traceStore, "a", 5000);
        RecordTurn(traceStore, "b", 1000);

        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageTimeseries, new { tzOffsetMinutes = 0 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        Assert.Equal(5000, resp.RootElement.GetProperty("result").GetProperty("longestTaskMs").GetInt64());
    }

    private static void RecordTurn(TraceStore store, string sessionKey, double durationMs)
    {
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.TurnCompleted,
            DurationMs = durationMs
        });
    }

    private static void RecordSession(
        TraceStore store, string sessionKey, string startedAtUtc, long input, long output)
    {
        var ts = DateTimeOffset.Parse(startedAtUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal);
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.TokenUsage,
            Timestamp = ts,
            InputTokens = input,
            OutputTokens = output
        });
    }
}
