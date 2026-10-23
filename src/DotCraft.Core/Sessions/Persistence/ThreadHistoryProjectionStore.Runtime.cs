using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    public async Task<(SessionThread Thread, IReadOnlyList<(long Ordinal, SessionTurn Turn)> Turns)> ReadPersistedStateAsync(
        string threadId,
        string rolloutPath,
        IReadOnlyCollection<string> turnIds,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        var thread = ReadSnapshot(threadId).Thread;
        using var connection = stateDatabase.OpenConnection();
        return (thread, ReadTurnRows(connection, threadId, turnIds));
    }

    public async Task<SessionThread> ReadWorkingSetAsync(string threadId, string rolloutPath, CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        var thread = ReadSnapshot(threadId).Thread;
        using var connection = stateDatabase.OpenConnection();
        var recent = new List<(long Ordinal, SessionTurn Turn)>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT rollout_ordinal, turn_json FROM thread_turns
                WHERE thread_id = $thread_id
                ORDER BY rollout_ordinal DESC
                """;
            command.Parameters.AddWithValue("$thread_id", threadId);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var turn = DeserializeTurn(reader.GetString(1));
                recent.Add((reader.GetInt64(0), turn));
                if (ThreadWorkingSet.IsTerminal(turn))
                    break;
            }
        }

        recent.Reverse();
        foreach (var (_, turn) in recent)
            LoadItems(connection, threadId, turn);
        var omitted = ReadAggregates(
            connection,
            threadId,
            "rollout_ordinal < $boundary",
            command => command.Parameters.AddWithValue("$boundary", recent.Count == 0 ? long.MaxValue : recent[0].Ordinal));
        thread.Turns = recent.Select(static entry => entry.Turn).ToList();
        thread.OmittedTurnCount = omitted.TurnCount;
        thread.OmittedTokenUsage = omitted.TokenUsage;
        return thread;
    }

    public async Task<IReadOnlyList<string>> ReadTurnIdsAsync(string threadId, string rolloutPath, CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT turn_id FROM thread_turns WHERE thread_id = $thread_id ORDER BY rollout_ordinal";
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
            ids.Add(reader.GetString(0));
        return ids;
    }

    public async Task<IReadOnlySet<string>> ReadExistingTurnIdsAsync(
        string threadId,
        string rolloutPath,
        IReadOnlyCollection<string> candidates,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turnId in candidates)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
            command.Parameters.AddWithValue("$thread_id", threadId);
            command.Parameters.AddWithValue("$turn_id", turnId);
            if (command.ExecuteScalar() is not null)
                existing.Add(turnId);
        }

        return existing;
    }

    public async Task<IReadOnlyList<SessionTurn>> ReadTurnsAsync(
        string threadId,
        string rolloutPath,
        IReadOnlyCollection<string> turnIds,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        return ReadTurnRows(connection, threadId, turnIds).Select(static entry => entry.Turn).ToList();
    }

    public async Task<SessionItem?> ReadItemAsync(
        string threadId,
        string rolloutPath,
        string turnId,
        string itemId,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_json FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $item_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$item_id", itemId);
        return command.ExecuteScalar() is string json ? DeserializeItem(json) : null;
    }

    public async Task<IReadOnlyList<ThreadHistoryItem>> ReadUnfinishedCommandItemsAsync(
        string threadId,
        string rolloutPath,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT turn_id, item_json FROM thread_items
            WHERE thread_id = $thread_id
              AND json_extract(item_json, '$.type') = $type
              AND json_extract(item_json, '$.status') <> $completed
            ORDER BY rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$type", ItemType.CommandExecution.ToString());
        command.Parameters.AddWithValue("$completed", ItemStatus.Completed.ToString());
        using var reader = command.ExecuteReader();
        var items = new List<ThreadHistoryItem>();
        while (reader.Read())
            items.Add(new ThreadHistoryItem(reader.GetString(0), DeserializeItem(reader.GetString(1))));
        return items;
    }

    public async Task<ThreadHistoryAggregates> ReadAggregatesAsync(
        string threadId,
        string rolloutPath,
        IReadOnlyCollection<string> excludedTurnIds,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        var excluded = excludedTurnIds.ToList();
        var filter = excluded.Count == 0
            ? "1 = 1"
            : $"turn_id NOT IN ({string.Join(", ", excluded.Select((_, index) => $"$excluded_{index}"))})";
        return ReadAggregates(connection, threadId, filter, command =>
        {
            for (var index = 0; index < excluded.Count; index++)
                command.Parameters.AddWithValue($"$excluded_{index}", excluded[index]);
        });
    }

    private static ThreadHistoryAggregates ReadAggregates(
        SqliteConnection connection,
        string threadId,
        string turnFilter,
        Action<SqliteCommand> bind)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT COUNT(*),
                   COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(cached_input_tokens), 0), COALESCE(SUM(cache_write_input_tokens), 0),
                   COALESCE(SUM(reasoning_output_tokens), 0), COALESCE(SUM(llm_call_count), 0),
                   COALESCE(SUM(total_tokens), 0),
                   (SELECT COUNT(*) FROM thread_items
                    WHERE thread_id = $thread_id AND tool_call = 1
                      AND turn_id IN (SELECT turn_id FROM thread_turns WHERE thread_id = $thread_id AND {turnFilter}))
            FROM thread_turns
            WHERE thread_id = $thread_id AND {turnFilter}
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        bind(command);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new ThreadHistoryAggregates(
            reader.GetInt32(0),
            new TokenUsageInfo
            {
                InputTokens = reader.GetInt64(1),
                OutputTokens = reader.GetInt64(2),
                CachedInputTokens = reader.GetInt64(3),
                CacheWriteInputTokens = reader.GetInt64(4),
                ReasoningOutputTokens = reader.GetInt64(5),
                LlmCallCount = reader.GetInt32(6),
                TotalTokens = reader.GetInt64(7)
            },
            reader.GetInt32(8));
    }

    private static List<(long Ordinal, SessionTurn Turn)> ReadTurnRows(
        SqliteConnection connection,
        string threadId,
        IReadOnlyCollection<string> turnIds)
    {
        var turns = new List<(long Ordinal, SessionTurn Turn)>(turnIds.Count);
        foreach (var turnId in turnIds)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT rollout_ordinal, turn_json FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
            command.Parameters.AddWithValue("$thread_id", threadId);
            command.Parameters.AddWithValue("$turn_id", turnId);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
                continue;
            var turn = DeserializeTurn(reader.GetString(1));
            LoadItems(connection, threadId, turn);
            turns.Add((reader.GetInt64(0), turn));
        }

        return turns.OrderBy(static entry => entry.Ordinal).ToList();
    }

    private static void LoadItems(SqliteConnection connection, string threadId, SessionTurn turn)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT item_json FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id
            ORDER BY rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turn.Id);
        using var reader = command.ExecuteReader();
        turn.Items = [];
        while (reader.Read())
            turn.Items.Add(DeserializeItem(reader.GetString(0)));
    }

    private static SessionTurn DeserializeTurn(string json) =>
        JsonSerializer.Deserialize<SessionTurn>(json, JsonOptions)
        ?? throw new JsonException("Projected Turn is empty.");

    private static SessionItem DeserializeItem(string json) =>
        JsonSerializer.Deserialize<SessionItem>(json, JsonOptions)
        ?? throw new JsonException("Projected Item is empty.");
}
