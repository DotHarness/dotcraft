namespace DotCraft.Tracing;

public sealed partial class UsageAnalyticsService
{
    public ProfileInsights GetProfileInsights(int topSkills = 5, int tzOffsetMinutes = 0, DateTimeOffset? now = null)
    {
        var range = UsageDateRange.Create(null, null, tzOffsetMinutes);
        var days = GetHistory(new UsageHistoryQuery(UsageMetric.Tokens, UsageDimension.None, range, 1)).Days;
        var active = days.Where(day => day.Total > 0).Select(day => day.Date).ToHashSet();
        var today = range.LocalDayOf(now ?? DateTimeOffset.UtcNow);
        var cursor = active.Contains(today) ? today : today.AddDays(-1);
        var currentStreak = 0;
        while (active.Contains(cursor))
        {
            currentStreak++;
            cursor = cursor.AddDays(-1);
        }
        var longestStreak = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in active.Order())
        {
            run = previous?.AddDays(1) == day ? run + 1 : 1;
            longestStreak = Math.Max(longestStreak, run);
            previous = day;
        }

        using var connection = _stateRuntime.OpenConnection();
        RankedUsage? Top(string column)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT {column}, COUNT(*), SUM(COUNT(*)) OVER ()
                FROM usage_events
                WHERE type = 'Response' AND {column} IS NOT NULL AND {column} <> ''
                GROUP BY {column} ORDER BY COUNT(*) DESC, {column} LIMIT 1
                """;
            using var reader = command.ExecuteReader();
            return reader.Read() ? new RankedUsage(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)) : null;
        }

        var model = Top("model_id");
        var reasoning = Top("reasoning_effort");
        long distinctSkills, skillUses, threads, longestTask;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    COUNT(DISTINCT CASE WHEN type = 'SkillReferenced' THEN NULLIF(tool_name, '') END),
                    SUM(CASE WHEN type = 'SkillReferenced' AND tool_name IS NOT NULL AND tool_name <> '' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN type = 'ThreadObserved' THEN 1 ELSE 0 END),
                    MAX(CASE WHEN type = 'TurnCompleted' THEN duration_ms ELSE 0 END)
                FROM usage_events
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            distinctSkills = ReadInt64(reader, 0);
            skillUses = ReadInt64(reader, 1);
            threads = ReadInt64(reader, 2);
            longestTask = ReadInt64(reader, 3);
        }
        var skills = new List<SkillUsageBucket>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT tool_name, COUNT(*), MAX(tool_source) FROM usage_events
                WHERE type = 'SkillReferenced' AND tool_name IS NOT NULL AND tool_name <> ''
                GROUP BY tool_name ORDER BY COUNT(*) DESC, tool_name LIMIT $limit
                """;
            command.Parameters.AddWithValue("$limit", Math.Clamp(topSkills, 1, 20));
            using var reader = command.ExecuteReader();
            while (reader.Read())
                skills.Add(new SkillUsageBucket(reader.GetString(0), reader.GetInt64(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }
        return new ProfileInsights(
            model, reasoning, (int)distinctSkills, skillUses, skills,
            (long)days.Sum(day => day.Total), (long)(days.Count == 0 ? 0 : days.Max(day => day.Total)),
            currentStreak, longestStreak, (int)threads, longestTask);
    }
}

public sealed record ProfileInsights(
    RankedUsage? TopModel,
    RankedUsage? TopReasoning,
    int DistinctSkillCount,
    long TotalSkillCount,
    IReadOnlyList<SkillUsageBucket> TopSkills,
    long LifetimeTokens,
    long PeakDailyTokens,
    int CurrentStreakDays,
    int LongestStreakDays,
    int TotalThreads,
    long LongestTaskMs);

public sealed record RankedUsage(string Key, long Count, long Total);

public sealed record SkillUsageBucket(string Name, long Count, string? ToolSource = null);
