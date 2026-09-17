using System.Globalization;
using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

/// <summary>
/// Aggregation contract of <see cref="UsageAnalyticsService"/> (spec §27A) over completed-Turn
/// usage facts and persisted trace events.
/// </summary>
public sealed class UsageAnalyticsServiceTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceStateDatabase _db;
    private readonly TokenUsageStore _facts;
    private readonly TraceStore _traces;
    private readonly UsageAnalyticsService _service;

    public UsageAnalyticsServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "usage-analytics-tests", Guid.NewGuid().ToString("N"));
        _db = new WorkspaceStateDatabase(Path.Combine(_root, ".craft"));
        _facts = new TokenUsageStore(_db);
        _traces = new TraceStore(_db, maxEventsPerSession: 5000, synchronousPersist: true);
        _service = new UsageAnalyticsService(_db);
    }

    [Fact]
    public void History_BucketsByLocalDay_AndFoldsGroupsBeyondTopLimit()
    {
        // 23:30 UTC on the 29th is 00:30 local on the 30th at UTC+1.
        RecordTurn("t1", "2026-05-29T23:30:00Z", input: 100, output: 10, model: "a");
        RecordTurn("t1", "2026-05-30T08:00:00Z", input: 50, output: 5, model: "b");
        RecordTurn("t1", "2026-05-30T09:00:00Z", input: 20, output: 2, model: "c");
        RecordTurn("t1", "2026-05-31T09:00:00Z", input: 1, output: 1, model: "c");

        var history = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Tokens,
            UsageDimension.Model,
            UsageDateRange.Create(new DateOnly(2026, 5, 30), new DateOnly(2026, 5, 31), 60),
            TopLimit: 2));

        Assert.Equal(UsageAnalyticsNames.UnitTokens, history.Unit);
        Assert.Equal(2, history.Days.Count);

        var first = history.Days[0];
        Assert.Equal(new DateOnly(2026, 5, 30), first.Date);
        Assert.Equal(187, first.Total);
        Assert.Equal(
            new[] { ("a", 110L), ("b", 55L), (UsageAnalyticsNames.OtherKey, 22L) },
            first.Values.Select(value => (value.Key, value.Value)).ToArray());

        var second = history.Days[1];
        Assert.Equal(new DateOnly(2026, 5, 31), second.Date);
        Assert.Equal(2, second.Total);
        Assert.Equal(UsageAnalyticsNames.OtherKey, Assert.Single(second.Values).Key);

        Assert.Equal(
            new[] { ("a", 110L), ("b", 55L), (UsageAnalyticsNames.OtherKey, 24L) },
            history.Series.Select(series => (series.Key, series.Total)).ToArray());
    }

    [Fact]
    public void History_TokenType_ReturnsFixedKeysWithoutFolding()
    {
        RecordTurn("t1", "2026-05-30T08:00:00Z", input: 100, output: 10, cached: 60, cacheWrite: 10);

        var history = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Tokens,
            UsageDimension.TokenType,
            UsageDateRange.Create(null, null, 0),
            TopLimit: 1));

        var day = Assert.Single(history.Days);
        Assert.Equal(110, day.Total);
        var values = day.Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(30, values[UsageAnalyticsNames.TokenTypeUncached]);
        Assert.Equal(60, values[UsageAnalyticsNames.TokenTypeCached]);
        Assert.Equal(10, values[UsageAnalyticsNames.TokenTypeCacheWrite]);
        Assert.Equal(10, values[UsageAnalyticsNames.TokenTypeOutput]);
        Assert.DoesNotContain(history.Series, series => series.Key == UsageAnalyticsNames.OtherKey);
    }

    [Fact]
    public void History_Surface_RollsSubagentTurnsIntoRootThreadOrigin()
    {
        InsertThread("root", "dotcraft-desktop");
        InsertThread("child", "subagent");
        RecordTurn("root", "2026-05-30T08:00:00Z", input: 10, output: 1, source: "dotcraft-desktop", rootThreadId: "root");
        RecordTurn("child", "2026-05-30T09:00:00Z", input: 20, output: 2, source: "subagent", rootThreadId: "root");
        RecordTurn("cli-thread", "2026-05-30T10:00:00Z", input: 5, output: 1, source: "cli", rootThreadId: "cli-thread");

        var history = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Tokens,
            UsageDimension.Surface,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit));

        var values = Assert.Single(history.Days).Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(33, values["dotcraft-desktop"]);
        Assert.Equal(6, values["cli"]);
        Assert.False(values.ContainsKey("subagent"));
    }

    [Fact]
    public void History_ToolCalls_GroupsByRecordedSource()
    {
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:00:00Z", toolName: "list_issues", toolSource: "mcp:github");
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:05:00Z", toolName: "list_issues", toolSource: "mcp:github");
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:10:00Z", toolName: "read_file", toolSource: ToolUsageSource.Builtin);
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:15:00Z", toolName: "unsourced");
        RecordEvent("t1", TraceEventType.ToolCallStarted, "2026-05-30T08:16:00Z", toolName: "read_file");

        var bySource = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.ToolCalls,
            UsageDimension.ToolSource,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit));

        Assert.Equal(UsageAnalyticsNames.UnitCount, bySource.Unit);
        var values = Assert.Single(bySource.Days).Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(2, values["mcp:github"]);
        Assert.Equal(1, values[ToolUsageSource.Builtin]);
        Assert.Equal(1, values[UsageAnalyticsNames.UnknownKey]);

        var total = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.ToolCalls,
            UsageDimension.None,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit));
        Assert.Equal(4, Assert.Single(total.Days).Total);
        Assert.Empty(total.Series);
    }

    [Fact]
    public void History_RejectsUnsupportedCombination()
    {
        Assert.Throws<ArgumentException>(() => _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Tokens,
            UsageDimension.Skill,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit)));
    }

    [Fact]
    public void Summary_AggregatesFactsAndEventsInsideRange()
    {
        RecordTurn("t1", "2026-05-29T08:00:00Z", input: 1000, output: 100, llmCalls: 3);
        RecordTurn("t1", "2026-05-30T08:00:00Z", input: 100, output: 20, cached: 60, cacheWrite: 10, llmCalls: 2);
        RecordTurn("t2", "2026-05-30T09:00:00Z", input: 40, output: 8, llmCalls: 1);
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:01:00Z", toolName: "a", durationMs: 100);
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-30T08:02:00Z", toolName: "b", durationMs: 300);
        RecordEvent("t1", TraceEventType.Error, "2026-05-30T08:03:00Z");
        RecordEvent("t1", TraceEventType.Response, "2026-05-30T08:04:00Z");
        RecordEvent("t1", TraceEventType.ToolCallCompleted, "2026-05-29T08:01:00Z", toolName: "old", durationMs: 5000);

        var summary = _service.GetSummary(UsageDateRange.Create(new DateOnly(2026, 5, 30), null, 0));

        Assert.Equal(2, summary.ThreadCount);
        Assert.Equal(3, summary.TotalRequests);
        Assert.Equal(1, summary.TotalResponses);
        Assert.Equal(2, summary.TotalToolCalls);
        Assert.Equal(400, summary.TotalToolDurationMs);
        Assert.Equal(300, summary.MaxToolDurationMs);
        Assert.Equal(1, summary.TotalErrors);
        Assert.Equal(140, summary.TotalInputTokens);
        Assert.Equal(28, summary.TotalOutputTokens);
        Assert.Equal(60, summary.TotalCachedInputTokens);
        Assert.Equal(70, summary.TotalFreshInputTokens);
        Assert.Equal(168, summary.TotalTokens);
        Assert.Equal(60d / 140d, summary.CacheHitRate, 6);
    }

    [Fact]
    public void TopThreads_RanksRecentlyActiveThreadsByLifetimeUsage()
    {
        InsertThread("big", "dotcraft-desktop", displayName: "Big thread");
        InsertThread("small", "cli", firstUserMessage: "hello there");
        RecordTurn("big", "2026-01-01T08:00:00Z", input: 900, output: 100, rootThreadId: "big");
        RecordTurn("big", "2026-05-30T08:00:00Z", input: 9, output: 1, rootThreadId: "big");
        RecordTurn("big-child", "2026-05-30T08:30:00Z", input: 5, output: 5, source: "subagent", rootThreadId: "big");
        RecordTurn("small", "2026-05-30T09:00:00Z", input: 90, output: 10, source: "cli", rootThreadId: "small");
        RecordTurn("stale", "2026-01-02T08:00:00Z", input: 5000, output: 1, rootThreadId: "stale");

        var threads = _service.GetTopThreads(UsageDateRange.Create(new DateOnly(2026, 5, 30), null, 0), limit: 5);

        Assert.Equal(new[] { "big", "small" }, threads.Select(thread => thread.ThreadId).ToArray());
        var big = threads[0];
        Assert.Equal("Big thread", big.Title);
        Assert.Equal("dotcraft-desktop", big.OriginChannel);
        Assert.Equal(3, big.Turns);
        Assert.Equal(1020, big.TotalTokens);
        Assert.Equal(DateTimeOffset.Parse("2026-05-30T08:30:00Z", CultureInfo.InvariantCulture), big.LastActiveAt);
        Assert.Equal("hello there", threads[1].Title);
    }

    [Fact]
    public void History_GroupsTurnsByReasoningAndSpeed()
    {
        RecordTurn("t1", "2026-05-30T08:00:00Z", input: 10, output: 0, reasoning: "high", speed: "fast");
        RecordTurn("t1", "2026-05-30T09:00:00Z", input: 20, output: 0, reasoning: "high", speed: "standard");
        RecordTurn("t1", "2026-05-30T10:00:00Z", input: 5, output: 0);

        var byReasoning = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Turns,
            UsageDimension.Reasoning,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit));
        var reasoning = Assert.Single(byReasoning.Days).Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(2, reasoning["high"]);
        Assert.Equal(1, reasoning[UsageAnalyticsNames.UnknownKey]);

        var bySpeed = _service.GetHistory(new UsageHistoryQuery(
            UsageMetric.Tokens,
            UsageDimension.Speed,
            UsageDateRange.Create(null, null, 0),
            UsageHistoryQuery.DefaultTopLimit));
        var speed = Assert.Single(bySpeed.Days).Values.ToDictionary(value => value.Key, value => value.Value);
        Assert.Equal(10, speed["fast"]);
        Assert.Equal(20, speed["standard"]);
        Assert.Equal(5, speed[UsageAnalyticsNames.UnknownKey]);
    }

    [Fact]
    public void ThreadUsage_GroupsByModelReasoningAndSpeed_IncludingSubagentTurns()
    {
        RecordTurn("root", "2026-05-30T08:00:00Z", input: 100, output: 10, cached: 50, model: "atlas-4", rootThreadId: "root", reasoning: "high", speed: "standard");
        RecordTurn("root", "2026-05-30T09:00:00Z", input: 40, output: 4, model: "atlas-4", rootThreadId: "root", reasoning: "high", speed: "standard");
        RecordTurn("child", "2026-05-30T09:30:00Z", input: 20, output: 2, model: "boreal-mini", source: "subagent", rootThreadId: "root");
        RecordTurn("other", "2026-05-30T10:00:00Z", input: 999, output: 1, rootThreadId: "other");

        var usage = _service.GetThreadUsage("root");

        Assert.Equal(3, usage.Turns);
        Assert.Equal(176, usage.TotalTokens);
        Assert.Equal(50, usage.CachedInputTokens);
        Assert.Equal(2, usage.Groups.Count);
        var top = usage.Groups[0];
        Assert.Equal(("atlas-4", "high", "standard", 2, 154L), (top.Model, top.ReasoningEffort, top.Speed, top.Turns, top.TotalTokens));
        var child = usage.Groups[1];
        Assert.Equal("boreal-mini", child.Model);
        Assert.Null(child.ReasoningEffort);
        Assert.Equal(22, child.TotalTokens);
    }

    private void RecordTurn(
        string threadId,
        string completedAtUtc,
        long input,
        long output,
        long cached = 0,
        long cacheWrite = 0,
        string? model = null,
        string source = "dotcraft-desktop",
        string? rootThreadId = null,
        int llmCalls = 1,
        string? reasoning = null,
        string? speed = null)
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
            RootThreadId = rootThreadId ?? threadId,
            Model = model,
            ReasoningEffort = reasoning,
            Speed = speed,
            LlmCallCount = llmCalls,
            InputTokens = input,
            OutputTokens = output,
            CachedInputTokens = cached,
            CacheWriteInputTokens = cacheWrite
        });
    }

    private void RecordEvent(
        string sessionKey,
        TraceEventType type,
        string atUtc,
        string? toolName = null,
        string? toolSource = null,
        double? durationMs = null)
    {
        _traces.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = type,
            Timestamp = DateTimeOffset.Parse(atUtc, CultureInfo.InvariantCulture),
            ToolName = toolName,
            ToolSource = toolSource,
            DurationMs = durationMs
        });
    }

    private void InsertThread(string threadId, string originChannel, string? displayName = null, string? firstUserMessage = null)
    {
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads (
                thread_id, rollout_path, workspace_path, origin_channel, display_name, first_user_message,
                status, created_at, updated_at, history_mode)
            VALUES ($id, $id, $workspace, $origin, $display, $first, 'active', $now, $now, 'default')
            """;
        command.Parameters.AddWithValue("$id", threadId);
        command.Parameters.AddWithValue("$workspace", _root);
        command.Parameters.AddWithValue("$origin", originChannel);
        command.Parameters.AddWithValue("$display", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$first", (object?)firstUserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", "2026-01-01T00:00:00.0000000Z");
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
