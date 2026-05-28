using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Conformance tests for the <c>usage/summary</c> method and its
/// <c>usageTelemetry</c> capability (spec Section 27A).
/// </summary>
public sealed class AppServerUsageSummaryTests
{
    [Fact]
    public async Task UsageSummary_ReturnsMethodNotFound_WhenTracingDisabled()
    {
        using var h = new AppServerTestHarness();
        var initDoc = await h.InitializeAsync();

        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.False(caps.TryGetProperty("usageTelemetry", out _));

        var msg = h.BuildRequest(AppServerMethods.UsageSummary, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsErrorResponse(resp, AppServerErrors.MethodNotFoundCode);
    }

    [Fact]
    public async Task UsageSummary_AdvertisesCapability_AndAggregatesAcrossSessions()
    {
        var traceStore = BuildTraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        var initDoc = await h.InitializeAsync();

        var caps = initDoc.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(caps.GetProperty("usageTelemetry").GetBoolean());

        var msg = h.BuildRequest(AppServerMethods.UsageSummary, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");

        Assert.Equal(2, result.GetProperty("sessionCount").GetInt32());
        Assert.Equal(2, result.GetProperty("totalRequests").GetInt32());
        Assert.Equal(2, result.GetProperty("totalToolCalls").GetInt32());
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

    [Fact]
    public async Task UsageSummary_ReturnsZeros_WhenNoSessionsTraced()
    {
        var traceStore = new TraceStore();
        using var h = new AppServerTestHarness(traceStore: traceStore);
        await h.InitializeAsync();

        var msg = h.BuildRequest(AppServerMethods.UsageSummary, new { });
        await h.ExecuteRequestAsync(msg);
        var resp = h.Transport.TryReadSent()!;

        AppServerTestHarness.AssertIsSuccessResponse(resp);
        var result = resp.RootElement.GetProperty("result");
        Assert.Equal(0, result.GetProperty("sessionCount").GetInt32());
        Assert.Equal(0, result.GetProperty("totalTokens").GetInt64());
        Assert.Equal(0d, result.GetProperty("cacheHitRate").GetDouble());
    }

    private static TraceStore BuildTraceStore()
    {
        // In-memory store (no storage path / state runtime) records synchronously.
        var store = new TraceStore();

        store.Record(new TraceEvent { SessionKey = "s1", Type = TraceEventType.Request, Content = "hello" });
        store.Record(new TraceEvent
        {
            SessionKey = "s1",
            Type = TraceEventType.TokenUsage,
            InputTokens = 100,
            OutputTokens = 20,
            CachedInputTokens = 60,
            CacheWriteInputTokens = 10,
            ReasoningOutputTokens = 5
        });
        store.Record(new TraceEvent { SessionKey = "s1", Type = TraceEventType.ToolCallCompleted, ToolName = "ReadFile", DurationMs = 50 });

        store.Record(new TraceEvent { SessionKey = "s2", Type = TraceEventType.Request, Content = "hi" });
        store.Record(new TraceEvent
        {
            SessionKey = "s2",
            Type = TraceEventType.TokenUsage,
            InputTokens = 40,
            OutputTokens = 8,
            CachedInputTokens = 0,
            CacheWriteInputTokens = 0
        });
        store.Record(new TraceEvent { SessionKey = "s2", Type = TraceEventType.ToolCallCompleted, ToolName = "EditFile", DurationMs = 30 });

        return store;
    }
}
