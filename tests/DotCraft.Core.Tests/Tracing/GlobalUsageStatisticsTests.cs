using DotCraft.Persistence;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class GlobalUsageStatisticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "global-usage", Guid.NewGuid().ToString("N"));
    private readonly UsageStatisticsDatabase _database;
    private readonly UsageStatisticsStore _first;
    private readonly UsageStatisticsStore _second;
    private readonly UsageAnalyticsService _analytics;

    public GlobalUsageStatisticsTests()
    {
        _database = new UsageStatisticsDatabase(Path.Combine(_root, "user"));
        _first = new UsageStatisticsStore(_database, Path.Combine(_root, "one"));
        _second = new UsageStatisticsStore(_database, Path.Combine(_root, "two"));
        _analytics = new UsageAnalyticsService(_database);
    }

    [Fact]
    public void WorkspacesShareTotalsWithoutCollidingTurnIdsOrDuplicatingRecords()
    {
        var record = Usage("same-thread", "turn_001", 30);
        _first.ObserveThread(Thread("same-thread", "one"));
        _second.ObserveThread(Thread("same-thread", "two"));
        _first.Record(record);
        _first.Record(record);
        _second.Record(record);

        Assert.Equal(60, _analytics.GetSummary(UsageDateRange.Create(null, null, 0)).TotalTokens);
        Assert.Equal(2, _analytics.GetProfileInsights().TotalThreads);
        var rows = _analytics.GetTopThreads(UsageDateRange.Create(null, null, 0), 10);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.WorkspacePath).Distinct().Count());
        Assert.Equal(30, Assert.Single(_first.GetSourceSummaries()).TotalTokens);
        Assert.Equal(30, _analytics.GetThreadUsage(Path.Combine(_root, "two"), "same-thread").TotalTokens);
    }

    [Fact]
    public async Task ConversationAndTraceDeletionRetainStatisticsAfterReopen()
    {
        using var state = new WorkspaceStateDatabase(Path.Combine(_root, "one", ".craft"));
        var threads = new ThreadStore(Path.Combine(_root, "one", ".craft"), state);
        var trace = new TraceStore(state, 5000, synchronousPersist: true, statistics: _first);
        var persistence = new SessionPersistenceService(threads, trace, _first, state);
        var root = Thread("parent", "one");
        root.DisplayName = "private title";
        var child = Thread("child", "one");
        child.OriginChannel = "subagent";
        child.Source = ThreadSource.ForSubAgent(new SubAgentThreadSource
        {
            RootThreadId = root.Id, ParentThreadId = root.Id, AgentPath = "/root/worker"
        });
        await threads.SaveThreadAsync(root);
        await threads.SaveThreadAsync(child);
        _first.ObserveThread(root);
        _first.ObserveThread(child);
        _first.Record(Usage(root.Id, "turn_001", 10, cost: 0.5m));
        _first.Record(Usage(child.Id, "turn_001", 20, root.Id));
        trace.BindThreadMainSession(root.Id);
        trace.BindChildSession(child.Id, root.Id, root.Id);
        foreach (var session in new[] { root.Id, child.Id })
        {
            trace.Record(new TraceEvent { SessionKey = session, Type = TraceEventType.ToolCallCompleted, ToolName = "read", DurationMs = 10 });
            trace.Record(new TraceEvent { SessionKey = session, Type = TraceEventType.SkillReferenced, ToolName = "demo" });
            trace.Record(new TraceEvent { SessionKey = session, Type = TraceEventType.Error, Content = "private error" });
        }
        trace.Record(new TraceEvent { SessionKey = root.Id, Type = TraceEventType.Response, ModelId = "model-a", Content = "private response" });
        trace.Record(new TraceEvent { SessionKey = root.Id, Type = TraceEventType.TurnCompleted, DurationMs = 1000 });
        await persistence.DeleteThreadCascadeAsync(child.Id);
        await persistence.DeleteThreadCascadeAsync(root.Id);
        trace.ClearAll();

        Assert.Null(await threads.LoadThreadAsync(root.Id));
        Assert.Null(await threads.LoadThreadAsync(child.Id));
        Assert.Empty(trace.GetEvents(root.Id));
        using var reopened = new UsageStatisticsDatabase(Path.Combine(_root, "user"));
        var analytics = new UsageAnalyticsService(reopened);
        var summary = analytics.GetSummary(UsageDateRange.Create(null, null, 0));
        Assert.Equal(30, summary.TotalTokens);
        Assert.Equal(0.5, summary.TotalCostUsd);
        Assert.Equal(2, summary.TotalToolCalls);
        Assert.Equal(2, summary.TotalErrors);
        var insights = analytics.GetProfileInsights();
        Assert.Equal(1, insights.TotalThreads);
        Assert.Equal(2, insights.TotalSkillCount);
        Assert.Equal("model-a", insights.TopModel?.Key);
        Assert.Equal(1000, insights.LongestTaskMs);
        Assert.Empty(analytics.GetTopThreads(UsageDateRange.Create(null, null, 0), 10));
        var errors = analytics.GetHistory(new UsageHistoryQuery(UsageMetric.Errors, UsageDimension.Surface, UsageDateRange.Create(null, null, 0), 10));
        Assert.Equal("desktop", Assert.Single(errors.Series).Key);
        using var connection = reopened.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM usage_threads";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM usage_turns WHERE subject_label IS NOT NULL";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void ProfileUsesWholeHistoryAndLocalDayStreak()
    {
        foreach (var (at, amount) in new[]
        {
            ("2024-01-01T23:30:00Z", 100L),
            ("2024-01-02T23:30:00Z", 200L),
            ("2024-01-03T23:30:00Z", 300L),
            ("2026-05-28T23:30:00Z", 10L),
            ("2026-05-29T23:30:00Z", 20L)
        })
            _first.Record(Usage("thread", at, amount, at: DateTimeOffset.Parse(at)));
        var profile = _analytics.GetProfileInsights(tzOffsetMinutes: 60, now: DateTimeOffset.Parse("2026-05-31T08:00:00Z"));
        Assert.Equal(630, profile.LifetimeTokens);
        Assert.Equal(300, profile.PeakDailyTokens);
        Assert.Equal(2, profile.CurrentStreakDays);
        Assert.Equal(3, profile.LongestStreakDays);
        var heatmap = _analytics.GetHistory(new UsageHistoryQuery(UsageMetric.Tokens, UsageDimension.None,
            UsageDateRange.Create(new DateOnly(2025, 5, 25), null, 60), 1));
        Assert.Equal(30, heatmap.Days.Sum(day => day.Total));
    }

    [Fact]
    public void CollectionOffDoesNotFreezeExistingIndexOrHideHistory()
    {
        var thread = Thread("thread", "one");
        _first.ObserveThread(thread);
        _first.Record(Usage(thread.Id, "turn_001", 10));
        var disabled = new UsageStatisticsStore(_database, Path.Combine(_root, "one"), collectionEnabled: false);
        disabled.Record(Usage(thread.Id, "turn_002", 100));
        disabled.ObserveThread(Thread("old-untracked", "one"));
        thread.DisplayName = "renamed";
        disabled.UpdateThread(thread);
        disabled.ArchiveThread(thread.Id);
        var row = Assert.Single(_analytics.GetTopThreads(UsageDateRange.Create(null, null, 0), 5));
        Assert.Equal("renamed", row.Title);
        Assert.True(row.Archived);
        Assert.Equal(10, row.TotalTokens);
        Assert.Equal(1, _analytics.GetProfileInsights().TotalThreads);
        disabled.DeleteThread(thread.Id);
        Assert.Equal(10, _analytics.GetSummary(UsageDateRange.Create(null, null, 0)).TotalTokens);
    }

    private SessionThread Thread(string id, string workspace) => new()
    {
        Id = id, WorkspacePath = Path.Combine(_root, workspace), OriginChannel = "desktop",
        CreatedAt = DateTimeOffset.UtcNow, LastActiveAt = DateTimeOffset.UtcNow
    };

    private static TokenUsageRecord Usage(string thread, string turn, long tokens, string? root = null, decimal? cost = null, DateTimeOffset? at = null) => new()
    {
        ThreadId = thread, TurnId = turn, RootThreadId = root ?? thread, InputTokens = tokens,
        SourceId = "desktop", SourceMode = TokenUsageSourceModes.ServerManaged,
        SubjectKind = TokenUsageSubjectKinds.Thread, SubjectId = thread, SubjectLabel = "not retained",
        Timestamp = at ?? DateTimeOffset.UtcNow, CostUsd = cost
    };

    public void Dispose()
    {
        _database.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
