using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed record ThreadHistoryAggregates(int TurnCount, TokenUsageInfo TokenUsage, int ToolCallCount);

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
        var turns = new List<(long Ordinal, SessionTurn Turn)>(turnIds.Count);
        foreach (var turnId in turnIds)
        {
            using var turnCommand = connection.CreateCommand();
            turnCommand.CommandText = "SELECT rollout_ordinal, turn_json FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
            turnCommand.Parameters.AddWithValue("$thread_id", threadId);
            turnCommand.Parameters.AddWithValue("$turn_id", turnId);
            using var turnReader = turnCommand.ExecuteReader();
            if (!turnReader.Read())
                continue;
            var ordinal = turnReader.GetInt64(0);
            var turn = JsonSerializer.Deserialize<SessionTurn>(turnReader.GetString(1), JsonOptions)
                ?? throw new JsonException("Projected Turn is empty.");

            using var itemCommand = connection.CreateCommand();
            itemCommand.CommandText = """
                SELECT item_json FROM thread_items
                WHERE thread_id = $thread_id AND turn_id = $turn_id
                ORDER BY rollout_ordinal
                """;
            itemCommand.Parameters.AddWithValue("$thread_id", threadId);
            itemCommand.Parameters.AddWithValue("$turn_id", turnId);
            using var itemReader = itemCommand.ExecuteReader();
            turn.Items = [];
            while (itemReader.Read())
            {
                turn.Items.Add(JsonSerializer.Deserialize<SessionItem>(itemReader.GetString(0), JsonOptions)
                    ?? throw new JsonException("Projected Item is empty."));
            }
            turns.Add((ordinal, turn));
        }

        return (thread, turns.OrderBy(static entry => entry.Ordinal).ToList());
    }

    public async Task<ThreadHistoryAggregates> ReadAggregatesAsync(
        string threadId,
        string rolloutPath,
        CancellationToken ct)
    {
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(input_tokens), 0), COALESCE(SUM(output_tokens), 0),
                   COALESCE(SUM(cached_input_tokens), 0), COALESCE(SUM(cache_write_input_tokens), 0),
                   COALESCE(SUM(reasoning_output_tokens), 0), COALESCE(SUM(llm_call_count), 0),
                   COALESCE(SUM(total_tokens), 0),
                   (SELECT COUNT(*) FROM thread_items WHERE thread_id = $thread_id AND tool_call = 1)
            FROM thread_turns
            WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
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
}
