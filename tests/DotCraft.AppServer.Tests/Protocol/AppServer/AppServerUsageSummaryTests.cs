using System.Globalization;
using DotCraft.AppServer;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Conformance tests for the <c>usage/summary</c> method and its
/// <c>usageTelemetry</c> capability (spec Section 27A.2).
/// </summary>
public sealed class AppServerUsageSummaryTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceStateDatabase _db;
    private readonly TokenUsageStore _facts;
    private readonly TraceStore _traceStore;
    private readonly UsageAnalyticsService _analytics;

    public AppServerUsageSummaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "appserver-usage-summary-tests", Guid.NewGuid().ToString("N"));
        _db = new WorkspaceStateDatabase(Path.Combine(_root, ".craft"));
        _facts = new TokenUsageStore(_db);
        _traceStore = new TraceStore(_db, maxEventsPerSession: 5000, synchronousPersist: true);
        _analytics = new UsageAnalyticsService(_db);
    }

    [Fact]
    public async Task UsageSummary_ReturnsMethodNotFound_WhenTracingDisabled()
    {
        using var h = new AppServerTestHarness();
        var initDoc = await h.InitializeAsync();

        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.False(caps.TryGetProperty("usageTelemetry", out _));

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageSummary, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task UsageSummary_AdvertisesCapability_AndAggregatesFactsAndEvents()
    {
        RecordTurn("t1", "2026-05-29T08:00:00Z", input: 100, output: 20, cached: 60, cacheWrite: 10, reasoning: 5);
        RecordTurn("t2", "2026-05-29T09:00:00Z", input: 40, output: 8);
        RecordToolCall("t1", "2026-05-29T08:01:00Z", durationMs: 100);
        RecordToolCall("t2", "2026-05-29T09:01:00Z", durationMs: 300);

        using var h = new AppServerTestHarness(traceStore: _traceStore, usageAnalytics: _analytics);
        var initDoc = await h.InitializeAsync();

        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(caps.GetProperty("usageTelemetry").GetBoolean());

        var msg = h.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.UsageSummary, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");

        Assert.Equal(2, result.GetProperty("sessionCount").GetInt32());
        Assert.Equal(2, result.GetProperty("totalRequests").GetInt32());
        Assert.Equal(2, result.GetProperty("totalToolCalls").GetInt32());
        Assert.Equal(400, result.GetProperty("totalToolDurationMs").GetInt64());
        Assert.Equal(300, result.GetProperty("maxToolDurationMs").GetInt64());
        Assert.Equal(200d, result.GetProperty("avgToolDurationMs").GetDouble(), 6);
        Assert.Equal(0, result.GetProperty("totalErrors").GetInt32());
        Assert.Equal(140, result.GetProperty("totalInputTokens").GetInt64());
        Assert.Equal(28, result.GetProperty("totalOutputTokens").GetInt64());
        Assert.Equal(60, result.GetProperty("totalCachedInputTokens").GetInt64());
        Assert.Equal(10, result.GetProperty("totalCacheWriteInputTokens").GetInt64());
        Assert.Equal(70, result.GetProperty("totalFreshInputTokens").GetInt64());
        Assert.Equal(5, result.GetProperty("totalReasoningOutputTokens").GetInt64());
        Assert.Equal(168, result.GetProperty("totalTokens").GetInt64());
        Assert.Equal(60d / 140d, result.GetProperty("cacheHitRate").GetDouble(), 6);
    }

    private void RecordTurn(
        string threadId,
        string completedAtUtc,
        long input,
        long output,
        long cached = 0,
        long cacheWrite = 0,
        long reasoning = 0)
    {
        _facts.Record(new TokenUsageRecord
        {
            Timestamp = DateTimeOffset.Parse(completedAtUtc, CultureInfo.InvariantCulture),
            SourceId = "dotcraft-desktop",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.Thread,
            SubjectId = threadId,
            SubjectLabel = threadId,
            ThreadId = threadId,
            SessionKey = threadId,
            RootThreadId = threadId,
            InputTokens = input,
            OutputTokens = output,
            CachedInputTokens = cached,
            CacheWriteInputTokens = cacheWrite,
            ReasoningOutputTokens = reasoning
        });
    }

    private void RecordToolCall(string sessionKey, string atUtc, double durationMs)
    {
        _traceStore.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = DateTimeOffset.Parse(atUtc, CultureInfo.InvariantCulture),
            ToolName = "tool",
            DurationMs = durationMs
        });
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
