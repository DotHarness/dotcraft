using System.Globalization;
using DotCraft.AppServer;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>Wire conformance of <c>usage/history</c>, <c>usage/threads</c>, and <c>usage/thread</c> (spec Sections 27A.3–27A.5).</summary>
public sealed class AppServerUsageHistoryTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceStateDatabase _db;
    private readonly TokenUsageStore _facts;
    private readonly TraceStore _traceStore;
    private readonly UsageAnalyticsService _analytics;

    public AppServerUsageHistoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "appserver-usage-history-tests", Guid.NewGuid().ToString("N"));
        _db = new WorkspaceStateDatabase(Path.Combine(_root, ".craft"));
        _facts = new TokenUsageStore(_db);
        _traceStore = new TraceStore(_db, maxEventsPerSession: 5000, synchronousPersist: true);
        _analytics = new UsageAnalyticsService(_db);
    }

    [Theory]
    [InlineData("credits", null, null)]
    [InlineData("tokens", "skill", null)]
    [InlineData("tokens", null, "2026/05/29")]
    public async Task UsageHistory_ReturnsInvalidParams_ForBadRequests(string metric, string? groupBy, string? from)
    {
        using var h = new AppServerTestHarness(traceStore: _traceStore, usageAnalytics: _analytics);
        await h.InitializeAsync();

        var msg = h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.UsageHistory,
            new { metric, groupBy, from });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task UsageHistory_ReturnsDailyGroupsAndSeries()
    {
        RecordTurn("t1", "2026-05-29T08:00:00Z", input: 100, output: 20, model: "atlas-4");
        RecordTurn("t1", "2026-05-29T18:00:00Z", input: 40, output: 8, model: "boreal-mini");
        RecordTurn("t2", "2026-05-31T10:00:00Z", input: 10, output: 2, model: "atlas-4");

        using var h = new AppServerTestHarness(traceStore: _traceStore, usageAnalytics: _analytics);
        var initDoc = await h.InitializeAsync();
        Assert.True(initDoc.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("usageTelemetry").GetBoolean());

        var msg = h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.UsageHistory,
            new { metric = "tokens", groupBy = "model", tzOffsetMinutes = 0 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal("tokens", result.GetProperty("unit").GetString());
        Assert.Equal("model", result.GetProperty("groupBy").GetString());

        var days = result.GetProperty("days");
        Assert.Equal(2, days.GetArrayLength());
        Assert.Equal("2026-05-29", days[0].GetProperty("date").GetString());
        Assert.Equal(168, days[0].GetProperty("total").GetInt64());
        var firstValues = days[0].GetProperty("values");
        Assert.Equal("atlas-4", firstValues[0].GetProperty("key").GetString());
        Assert.Equal(120, firstValues[0].GetProperty("value").GetInt64());
        Assert.Equal("boreal-mini", firstValues[1].GetProperty("key").GetString());
        Assert.Equal("2026-05-31", days[1].GetProperty("date").GetString());

        var series = result.GetProperty("series");
        Assert.Equal("atlas-4", series[0].GetProperty("key").GetString());
        Assert.Equal(132, series[0].GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task UsageThreads_RanksActiveThreadsByLifetimeUsage()
    {
        RecordTurn("big", "2026-01-01T08:00:00Z", input: 900, output: 100);
        RecordTurn("big", "2026-05-30T08:00:00Z", input: 9, output: 1);
        RecordTurn("small", "2026-05-30T09:00:00Z", input: 90, output: 10, source: "cli");
        RecordTurn("stale", "2026-01-02T08:00:00Z", input: 5000, output: 1);

        using var h = new AppServerTestHarness(traceStore: _traceStore, usageAnalytics: _analytics);
        await h.InitializeAsync();

        var msg = h.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.UsageThreads,
            new { from = "2026-05-30", tzOffsetMinutes = 0, limit = 5 });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var threads = resp.RootElement.GetProperty("result").GetProperty("threads");
        Assert.Equal(2, threads.GetArrayLength());

        var big = threads[0];
        Assert.Equal("big", big.GetProperty("threadId").GetString());
        Assert.False(big.TryGetProperty("title", out _));
        Assert.Equal("dotcraft-desktop", big.GetProperty("originChannel").GetString());
        Assert.Equal(2, big.GetProperty("turns").GetInt32());
        Assert.Equal(1010, big.GetProperty("totalTokens").GetInt64());
        Assert.False(big.GetProperty("archived").GetBoolean());
        Assert.Equal("small", threads[1].GetProperty("threadId").GetString());
        Assert.Equal("cli", threads[1].GetProperty("originChannel").GetString());
    }

    [Fact]
    public async Task UsageThread_BreaksDownLifetimeUsageByModel()
    {
        RecordTurn("t1", "2026-05-29T08:00:00Z", input: 100, output: 20, model: "atlas-4");
        RecordTurn("t1", "2026-05-30T08:00:00Z", input: 40, output: 8, model: "boreal-mini");
        RecordTurn("t2", "2026-05-30T09:00:00Z", input: 999, output: 1, model: "atlas-4");

        using var h = new AppServerTestHarness(traceStore: _traceStore, usageAnalytics: _analytics);
        await h.InitializeAsync();

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageThread, new { threadId = "t1" });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal("t1", result.GetProperty("threadId").GetString());
        Assert.Equal(2, result.GetProperty("turns").GetInt32());
        Assert.Equal(168, result.GetProperty("totalTokens").GetInt64());
        var groups = result.GetProperty("groups");
        Assert.Equal(2, groups.GetArrayLength());
        Assert.Equal("atlas-4", groups[0].GetProperty("model").GetString());
        Assert.Equal(120, groups[0].GetProperty("totalTokens").GetInt64());
        Assert.False(groups[0].TryGetProperty("reasoningEffort", out _));
        Assert.Equal("boreal-mini", groups[1].GetProperty("model").GetString());

        var missing = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageThread, new { });
        await h.ExecuteRequestAsync(missing);
        AppServerTestHarness.AssertIsErrorResponse(h.Transport.TryReadSent()!, AppServerErrors.InvalidParamsCode);
    }

    private void RecordTurn(
        string threadId,
        string completedAtUtc,
        long input,
        long output,
        string? model = null,
        string source = "dotcraft-desktop")
    {
        _facts.Record(new TokenUsageRecord
        {
            Timestamp = DateTimeOffset.Parse(completedAtUtc, CultureInfo.InvariantCulture),
            SourceId = source,
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.Thread,
            SubjectId = threadId,
            SubjectLabel = threadId,
            ThreadId = threadId,
            SessionKey = threadId,
            RootThreadId = threadId,
            Model = model,
            InputTokens = input,
            OutputTokens = output
        });
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
