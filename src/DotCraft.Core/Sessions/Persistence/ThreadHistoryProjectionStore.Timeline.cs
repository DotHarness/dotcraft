using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    public Task<ThreadHistoryBootstrap> ReadBootstrapAsync(
        string threadId, string rolloutPath, bool repair, CancellationToken ct) =>
        ReadRetrievalAsync(threadId, rolloutPath, repair, "bootstrap",
            () => QueryBootstrap(threadId, ct), static _ => 1, ct);

    public Task<ThreadTimelinePage> ListTimelineAsync(
        string threadId, string rolloutPath, ThreadTimelineCursor? cursor, int limit, bool repair, CancellationToken ct) =>
        ReadRetrievalAsync(threadId, rolloutPath, repair, "timeline",
            () => QueryTimeline(threadId, cursor, limit, ct), static page => page.Data.Count, ct);

    private ThreadHistoryBootstrap QueryBootstrap(string threadId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT next_rollout_ordinal - 1,
                   EXISTS(SELECT 1 FROM thread_turns WHERE thread_id = $thread_id),
                   EXISTS(SELECT 1 FROM thread_items WHERE thread_id = $thread_id)
            FROM thread_history_projection_state WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Thread history projection state is missing.");
        var upper = reader.GetInt64(0);
        var anchor = new ThreadHistoryCursor(upper, Inclusive: true, SnapshotOrdinal: upper);
        return new(reader.GetBoolean(1) ? anchor : null, reader.GetBoolean(2) ? anchor : null);
    }

    private ThreadTimelinePage QueryTimeline(string threadId, ThreadTimelineCursor? cursor, int limit, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH bounds AS MATERIALIZED (
                SELECT COALESCE($snapshot, next_rollout_ordinal - 1) AS upper
                FROM thread_history_projection_state WHERE thread_id = $thread_id
            ),
            items AS (
                SELECT rollout_ordinal AS position, 0 AS kind, turn_id, item_id
                FROM thread_items
                WHERE thread_id = $thread_id AND rollout_ordinal <= (SELECT upper FROM bounds)
                  AND rollout_ordinal <= $position
                  AND ($has_cursor = 0 OR (rollout_ordinal, 0, turn_id, item_id) < ($position, $kind, $turn_id, $item_id))
                ORDER BY rollout_ordinal DESC, turn_id DESC, item_id DESC LIMIT $limit
            ),
            starts AS (
                SELECT rollout_ordinal AS position, 1 AS kind, turn_id, '' AS item_id
                FROM thread_turns
                WHERE thread_id = $thread_id AND rollout_ordinal <= (SELECT upper FROM bounds)
                  AND rollout_ordinal <= $position
                  AND ($has_cursor = 0 OR (rollout_ordinal, 1, turn_id, '') < ($position, $kind, $turn_id, $item_id))
                ORDER BY rollout_ordinal DESC, turn_id DESC LIMIT $limit
            ),
            completions AS (
                SELECT completed_rollout_ordinal AS position, 2 AS kind, turn_id, '' AS item_id
                FROM thread_turns
                WHERE thread_id = $thread_id AND completed_rollout_ordinal IS NOT NULL
                  AND completed_rollout_ordinal <= (SELECT upper FROM bounds)
                  AND completed_rollout_ordinal <= $position
                  AND ($has_cursor = 0 OR (completed_rollout_ordinal, 2, turn_id, '') < ($position, $kind, $turn_id, $item_id))
                ORDER BY completed_rollout_ordinal DESC, turn_id DESC LIMIT $limit
            ),
            candidates AS (
                SELECT * FROM items UNION ALL SELECT * FROM starts UNION ALL SELECT * FROM completions
            ),
            page AS MATERIALIZED (
                SELECT * FROM candidates
                ORDER BY position DESC, kind DESC, turn_id DESC, item_id DESC LIMIT $limit
            )
            SELECT p.position, p.kind, p.turn_id, p.item_id,
                   CASE WHEN p.kind <> 0 THEN t.turn_json END, i.item_json, b.upper
            FROM page p
            JOIN thread_turns t ON t.thread_id = $thread_id AND t.turn_id = p.turn_id
            LEFT JOIN thread_items i ON p.kind = 0 AND i.thread_id = $thread_id
                AND i.turn_id = p.turn_id AND i.item_id = p.item_id
            CROSS JOIN bounds b
            ORDER BY p.position DESC, p.kind DESC, p.turn_id DESC, p.item_id DESC
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$snapshot", (object?)cursor?.SnapshotOrdinal ?? DBNull.Value);
        command.Parameters.AddWithValue("$position", cursor?.Position ?? long.MaxValue);
        command.Parameters.AddWithValue("$kind", (int?)cursor?.Kind ?? 0);
        command.Parameters.AddWithValue("$turn_id", cursor?.TurnId ?? "");
        command.Parameters.AddWithValue("$item_id", cursor?.ItemId ?? "");
        command.Parameters.AddWithValue("$has_cursor", cursor is null ? 0 : 1);
        command.Parameters.AddWithValue("$limit", limit + 1);
        using var reader = command.ExecuteReader();
        var entries = new List<ThreadTimelineEntry>(limit + 1);
        long upper = 0;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var turn = reader.IsDBNull(4) ? null :
                JsonSerializer.Deserialize<SessionTurn>(reader.GetString(4), JsonOptions)
                    ?? throw new JsonException("Projected Turn is empty.");
            entries.Add(new((ThreadTimelineEntryKind)reader.GetInt32(1), reader.GetInt64(0),
                reader.GetString(2), turn, reader.IsDBNull(5) ? null : DeserializeItem(reader.GetString(5))));
            upper = reader.GetInt64(6);
        }
        var hasMore = entries.Count > limit;
        if (hasMore)
            entries.RemoveAt(entries.Count - 1);
        var oldest = entries.LastOrDefault();
        var next = hasMore && oldest is not null
            ? new ThreadTimelineCursor(oldest.Position, oldest.Kind, oldest.TurnId, oldest.Item?.Id ?? "", upper)
            : null;
        entries.Reverse();
        return new(entries, next);
    }
}
