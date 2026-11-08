using System.Text;
using DotCraft.Persistence;

namespace DotCraft.Tracing;

public sealed partial class UsageAnalyticsService
{
    public IReadOnlyList<UsageThreadUsage> GetTopThreads(UsageDateRange range, int limit)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT t.workspace_path, t.thread_id, t.title, t.origin_channel, t.archived,
                MAX(d.timestamp), COUNT(*), SUM(d.input_tokens), SUM(d.output_tokens),
                SUM(d.cached_input_tokens), SUM(d.cost_usd)
            FROM usage_turns d
            JOIN usage_threads t ON t.workspace_key = d.workspace_key AND t.thread_id = d.root_thread_id
            WHERE t.thread_id = t.root_thread_id
            GROUP BY t.workspace_key, t.thread_id
            """);
        AppendRange(sql, command, range, "MAX(d.timestamp)", hasWhere: false, keyword: "HAVING");
        sql.Append(" ORDER BY SUM(d.input_tokens) + SUM(d.output_tokens) DESC, MAX(d.timestamp) DESC LIMIT $limit");
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 50));
        command.CommandText = sql.ToString();
        var rows = new List<UsageThreadUsage>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            rows.Add(new UsageThreadUsage(
                reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), ParseTimestamp(reader.GetString(5)), ReadInt32(reader, 6),
                ReadInt64(reader, 7), ReadInt64(reader, 8), ReadInt64(reader, 9),
                ReadOptionalDouble(reader, 10), reader.GetBoolean(4)));
        return rows;
    }

    public UsageThreadBreakdown GetThreadUsage(string workspacePath, string threadId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model, reasoning_effort, speed, COUNT(*), SUM(input_tokens), SUM(output_tokens),
                SUM(cached_input_tokens), SUM(cost_usd)
            FROM usage_turns
            WHERE workspace_key = $workspace AND root_thread_id = $thread
            GROUP BY model, reasoning_effort, speed
            ORDER BY SUM(input_tokens) + SUM(output_tokens) DESC, model, reasoning_effort, speed
            """;
        command.Parameters.AddWithValue("$workspace", UsageStatisticsDatabase.WorkspaceKey(workspacePath));
        command.Parameters.AddWithValue("$thread", threadId);
        var groups = new List<UsageThreadBreakdownGroup>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            groups.Add(new UsageThreadBreakdownGroup(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ReadInt32(reader, 3), ReadInt64(reader, 4), ReadInt64(reader, 5),
                ReadInt64(reader, 6), ReadOptionalDouble(reader, 7)));
        var priced = groups.Where(group => group.CostUsd is not null).ToList();
        return new UsageThreadBreakdown(workspacePath, threadId, groups.Sum(group => group.Turns),
            groups.Sum(group => group.InputTokens), groups.Sum(group => group.OutputTokens),
            groups.Sum(group => group.CachedInputTokens),
            priced.Count == 0 ? null : priced.Sum(group => group.CostUsd!.Value), groups);
    }
}
