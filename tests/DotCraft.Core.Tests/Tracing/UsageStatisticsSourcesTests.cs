using DotCraft.Persistence;
using DotCraft.Tracing;
using Xunit;

namespace DotCraft.Tests.Tracing;

public sealed class UsageStatisticsSourcesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "usage-sources", Guid.NewGuid().ToString("N"));
    private readonly UsageStatisticsDatabase _statistics;

    public UsageStatisticsSourcesTests() => _statistics = new UsageStatisticsDatabase(Path.Combine(_root, "user"));

    [Fact]
    public void RoundTripsSourceAndGroupUsage()
    {
        var writer = new UsageStatisticsStore(_statistics, _root);
        writer.Record(new TokenUsageRecord
        {
            TurnId = "turn_001",
            SourceId = "sample-channel",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u1",
            SubjectLabel = "Alice",
            ThreadId = "thread-1",
            InputTokens = 3,
            OutputTokens = 5,
            CachedInputTokens = 1,
            CacheWriteInputTokens = 1,
            ReasoningOutputTokens = 2,
            LlmCallCount = 2
        });
        writer.Record(new TokenUsageRecord
        {
            TurnId = "turn_001",
            SourceId = "sample-channel",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u2",
            SubjectLabel = "Bob",
            ContextKind = TokenUsageContextKinds.Group,
            ContextId = "42",
            ContextLabel = "Team",
            ThreadId = "thread-2",
            InputTokens = 7,
            OutputTokens = 11,
            CachedInputTokens = 4,
            CacheWriteInputTokens = 2,
            ReasoningOutputTokens = 3,
            LlmCallCount = 3
        });

        var reader = new UsageStatisticsStore(_statistics, _root);

        var summary = Assert.Single(reader.GetSourceSummaries());
        Assert.Equal("sample-channel", summary.SourceId);
        Assert.Equal(26, summary.TotalTokens);
        Assert.Equal(5, summary.TotalCachedInputTokens);
        Assert.Equal(3, summary.TotalCacheWriteInputTokens);
        Assert.Equal(2, summary.TotalFreshInputTokens);
        Assert.Equal(5, summary.TotalNonCachedInputTokens);
        Assert.Equal(5, summary.TotalReasoningOutputTokens);
        Assert.Equal(0.5, summary.CacheHitRate);
        Assert.Equal(2, summary.RequestCount);
        Assert.Equal(5, summary.LlmCallCount);
        Assert.Equal(2, summary.SubjectCount);
        Assert.Equal(1, summary.ContextCount);
        Assert.Equal(TokenUsageSubjectKinds.User, summary.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Group, summary.ContextKind);

        Assert.Equal(2, reader.GetSubjectBreakdown("sample-channel").Count);
        var context = Assert.Single(reader.GetContextBreakdown("sample-channel"));
        Assert.Equal("42", context.Id);
        Assert.Equal("Team", context.Label);
        Assert.Equal(1, context.RelatedSubjectCount);
        Assert.Equal(4, context.TotalCachedInputTokens);
        Assert.Equal(2, context.TotalCacheWriteInputTokens);
        Assert.Equal(3, context.LlmCallCount);
    }

    [Fact]
    public void AggregatesMixedKindsAndSortsBreakdowns()
    {
        var writer = new UsageStatisticsStore(_statistics, _root);
        writer.Record(new TokenUsageRecord
        {
            TurnId = "turn_001",
            ThreadId = "thread-1",
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 0, 0, TimeSpan.Zero),
            SourceId = "mixed",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "user-2",
            SubjectLabel = "Zulu",
            ContextKind = TokenUsageContextKinds.Group,
            ContextId = "group-1",
            ContextLabel = "Team",
            InputTokens = 2,
            OutputTokens = 3
        });
        writer.Record(new TokenUsageRecord
        {
            TurnId = "turn_001",
            ThreadId = "thread-2",
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 5, 0, TimeSpan.Zero),
            SourceId = "mixed",
            SourceMode = TokenUsageSourceModes.ClientManaged,
            SubjectKind = TokenUsageSubjectKinds.Session,
            SubjectId = "session-1",
            SubjectLabel = "Alpha",
            ContextKind = "room",
            ContextId = "room-1",
            ContextLabel = "Room",
            InputTokens = 4,
            OutputTokens = 1
        });
        writer.Record(new TokenUsageRecord
        {
            TurnId = "turn_001",
            ThreadId = "thread-3",
            Timestamp = new DateTimeOffset(2026, 4, 24, 8, 10, 0, TimeSpan.Zero),
            SourceId = "apple",
            SourceMode = TokenUsageSourceModes.ClientManaged,
            SubjectKind = TokenUsageSubjectKinds.Session,
            SubjectId = "session-2",
            SubjectLabel = "Apple Session",
            InputTokens = 5,
            OutputTokens = 5
        });

        var reader = new UsageStatisticsStore(_statistics, _root);

        var summaries = reader.GetSourceSummaries();
        Assert.Equal(["apple", "mixed"], summaries.Select(summary => summary.SourceId).ToArray());
        Assert.Null(summaries[0].ContextKind);

        var mixed = summaries[1];
        Assert.Equal(TokenUsageSourceModes.Mixed, mixed.SourceMode);
        Assert.Equal(TokenUsageSubjectKinds.Mixed, mixed.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Mixed, mixed.ContextKind);
        Assert.Equal(2, mixed.SubjectCount);
        Assert.Equal(2, mixed.ContextCount);
        Assert.Equal(2, mixed.RequestCount);
        Assert.Equal(10, mixed.TotalTokens);

        var subjects = reader.GetSubjectBreakdown("MIXED");
        Assert.Collection(
            subjects,
            first =>
            {
                Assert.Equal(TokenUsageSubjectKinds.Session, first.Kind);
                Assert.Equal("session-1", first.Id);
                Assert.Equal("Alpha", first.Label);
                Assert.Equal(5, first.TotalTokens);
            },
            second =>
            {
                Assert.Equal(TokenUsageSubjectKinds.User, second.Kind);
                Assert.Equal("user-2", second.Id);
                Assert.Equal("Zulu", second.Label);
                Assert.Equal(5, second.TotalTokens);
            });

        var contexts = reader.GetContextBreakdown("mixed");
        Assert.Collection(
            contexts,
            first =>
            {
                Assert.Equal("room", first.Kind);
                Assert.Equal("room-1", first.Id);
                Assert.Equal("Room", first.Label);
                Assert.Equal(1, first.RelatedSubjectCount);
                Assert.Equal(5, first.TotalTokens);
            },
            second =>
            {
                Assert.Equal(TokenUsageContextKinds.Group, second.Kind);
                Assert.Equal("group-1", second.Id);
                Assert.Equal("Team", second.Label);
                Assert.Equal(1, second.RelatedSubjectCount);
                Assert.Equal(5, second.TotalTokens);
            });
    }


    public void Dispose()
    {
        _statistics.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
