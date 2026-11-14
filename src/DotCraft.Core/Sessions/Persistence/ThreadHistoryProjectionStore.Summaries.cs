using DotCraft.Tools;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    public Task<ThreadHistoryPage<ThreadTurnSummary>> ListSummariesAsync(
        string threadId,
        string rolloutPath,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        IReadOnlyList<ToolName> summaryTools,
        bool repair,
        CancellationToken ct) =>
        ReadRetrievalAsync(
            threadId, rolloutPath, repair, "summaries",
            () => QuerySummaries(threadId, cursor, limit, direction, summaryTools, ct),
            static page => page.Data.Count, ct);

    public Task<ThreadHistoryCursor?> ResolveTurnCursorAsync(
        string threadId, string rolloutPath, string turnId, bool repair, CancellationToken ct) =>
        ReadRetrievalAsync(threadId, rolloutPath, repair, "turn_cursor",
            () =>
            {
                ct.ThrowIfCancellationRequested();
                using var connection = stateDatabase.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT rollout_ordinal FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
                command.Parameters.AddWithValue("$thread_id", threadId);
                command.Parameters.AddWithValue("$turn_id", turnId);
                return command.ExecuteScalar() is long ordinal ? new ThreadHistoryCursor(ordinal, Inclusive: true) : (ThreadHistoryCursor?)null;
            }, static cursor => cursor.HasValue ? 1 : 0, ct);

    public Task<ThreadHistoryCursor?> ResolveItemCursorAsync(
        string threadId,
        string rolloutPath,
        string turnId,
        string itemId,
        bool repair,
        CancellationToken ct) =>
        ReadRetrievalAsync(
            threadId, rolloutPath, repair, "item_cursor",
            () => QueryItemCursor(threadId, turnId, itemId, ct),
            static cursor => cursor.HasValue ? 1 : 0, ct);

    private ThreadHistoryCursor? QueryItemCursor(string threadId, string turnId, string itemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rollout_ordinal FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $item_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$item_id", itemId);
        return command.ExecuteScalar() is long ordinal ? new ThreadHistoryCursor(ordinal) : null;
    }

    private ThreadHistoryPage<ThreadTurnSummary> QuerySummaries(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        IReadOnlyList<ToolName> summaryTools,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var page = QueryTurns(connection, transaction, threadId, cursor, limit, direction);
        var summaries = new List<ThreadTurnSummary>(page.Data.Count);
        foreach (var turn in page.Data)
        {
            ct.ThrowIfCancellationRequested();
            summaries.Add(ReadSummary(connection, transaction, threadId, turn, summaryTools, ct, cursor?.SnapshotOrdinal));
        }
        transaction.Commit();
        return new(summaries, page.NextCursor, page.BackwardsCursor);
    }

    private static ThreadTurnSummary ReadSummary(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId,
        SessionTurn turn,
        IReadOnlyList<ToolName> summaryTools,
        CancellationToken ct,
        long? snapshotOrdinal = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var named = summaryTools.Count == 0
            ? "0"
            : string.Join(" OR ", summaryTools.Select((_, index) =>
                $"(summary_namespace = $namespace_{index} AND summary_tool_name = $name_{index})"));
        command.CommandText = $"""
            WITH metadata AS MATERIALIZED (
                SELECT item_id, rollout_ordinal, summary_kind, summary_call_id,
                       summary_namespace, summary_tool_name
                FROM thread_items
                WHERE thread_id = $thread_id AND turn_id = $turn_id AND rollout_ordinal <= $snapshot
            ),
            kept_calls AS MATERIALIZED (
                SELECT DISTINCT summary_call_id FROM metadata
                WHERE (summary_kind & $file_change) <> 0
                   OR ((summary_kind & $calls) <> 0 AND ({named}))
            ),
            selected AS (
                SELECT item_id, rollout_ordinal FROM metadata
                WHERE (summary_kind & $retained) <> 0
                   OR ((summary_kind & $paired) <> 0
                       AND summary_call_id IN (SELECT summary_call_id FROM kept_calls))
            ),
            totals AS (
                SELECT COUNT(*) AS item_count,
                       COALESCE(SUM((summary_kind & $tool_execution) <> 0), 0) AS tool_execution_count
                FROM metadata
            )
            SELECT totals.item_count, totals.tool_execution_count, items.item_json
            FROM totals
            LEFT JOIN selected ON 1 = 1
            LEFT JOIN thread_items items
                ON items.thread_id = $thread_id AND items.turn_id = $turn_id
               AND items.item_id = selected.item_id
            ORDER BY selected.rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turn.Id);
        command.Parameters.AddWithValue("$snapshot", snapshotOrdinal ?? long.MaxValue);
        command.Parameters.AddWithValue("$retained", (int)ThreadSummaryKind.Retained);
        command.Parameters.AddWithValue("$file_change", (int)ThreadSummaryKind.FileChange);
        command.Parameters.AddWithValue("$calls", (int)(ThreadSummaryKind.ToolCall | ThreadSummaryKind.DynamicToolCall));
        command.Parameters.AddWithValue("$paired", (int)(ThreadSummaryKind.ToolCall | ThreadSummaryKind.ToolResult | ThreadSummaryKind.DynamicToolCall));
        command.Parameters.AddWithValue("$tool_execution", (int)ThreadSummaryKind.ToolExecution);
        for (var index = 0; index < summaryTools.Count; index++)
        {
            command.Parameters.AddWithValue($"$namespace_{index}", summaryTools[index].Namespace ?? "");
            command.Parameters.AddWithValue($"$name_{index}", summaryTools[index].Name);
        }

        using var reader = command.ExecuteReader();
        var itemCount = 0;
        var toolExecutionCount = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            itemCount = reader.GetInt32(0);
            toolExecutionCount = reader.GetInt32(1);
            if (!reader.IsDBNull(2))
                turn.Items.Add(DeserializeItem(reader.GetString(2)));
        }
        return new(turn, itemCount) { ToolExecutionCount = toolExecutionCount };
    }
}
