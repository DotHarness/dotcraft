using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjector
{
    private void UpsertTurn(SessionTurn turn, bool preserveOrdinal)
    {
        var ordinal = preserveOrdinal ? LoadTurnOrdinal(turn.Id) : null;
        ordinal ??= NextOrdinal++;
        turn.Items = [];
        var usage = turn.TokenUsage;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_turns (
                thread_id, turn_id, rollout_ordinal, turn_json,
                input_tokens, output_tokens, cached_input_tokens, cache_write_input_tokens,
                reasoning_output_tokens, llm_call_count, total_tokens
            ) VALUES (
                $thread_id, $turn_id, $ordinal, $json,
                $input_tokens, $output_tokens, $cached_input_tokens, $cache_write_input_tokens,
                $reasoning_output_tokens, $llm_call_count, $total_tokens
            )
            ON CONFLICT(thread_id, turn_id) DO UPDATE SET
                turn_json = excluded.turn_json,
                input_tokens = excluded.input_tokens,
                output_tokens = excluded.output_tokens,
                cached_input_tokens = excluded.cached_input_tokens,
                cache_write_input_tokens = excluded.cache_write_input_tokens,
                reasoning_output_tokens = excluded.reasoning_output_tokens,
                llm_call_count = excluded.llm_call_count,
                total_tokens = excluded.total_tokens
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turn.Id);
        command.Parameters.AddWithValue("$ordinal", ordinal.Value);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(turn, JsonOptions));
        command.Parameters.AddWithValue("$input_tokens", usage?.InputTokens ?? 0);
        command.Parameters.AddWithValue("$output_tokens", usage?.OutputTokens ?? 0);
        command.Parameters.AddWithValue("$cached_input_tokens", usage?.CachedInputTokens ?? 0);
        command.Parameters.AddWithValue("$cache_write_input_tokens", usage?.CacheWriteInputTokens ?? 0);
        command.Parameters.AddWithValue("$reasoning_output_tokens", usage?.ReasoningOutputTokens ?? 0);
        command.Parameters.AddWithValue("$llm_call_count", usage?.LlmCallCount ?? 0);
        command.Parameters.AddWithValue("$total_tokens", usage?.TotalTokens ?? 0);
        command.ExecuteNonQuery();
    }

    private void UpsertItem(string turnId, SessionItem item)
    {
        var existing = LoadItemOrdinal(turnId, item.Id);
        var position = existing ?? NextOrdinal;
        var updated = NextOrdinal++;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_items (
                thread_id, turn_id, item_id, rollout_ordinal, updated_rollout_ordinal, item_json,
                tool_call, user_text
            ) VALUES ($thread_id, $turn_id, $item_id, $ordinal, $updated, $json, $tool_call, $user_text)
            ON CONFLICT(thread_id, turn_id, item_id) DO UPDATE SET
                updated_rollout_ordinal = excluded.updated_rollout_ordinal,
                item_json = excluded.item_json,
                tool_call = excluded.tool_call,
                user_text = excluded.user_text
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$item_id", item.Id);
        command.Parameters.AddWithValue("$ordinal", position);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(item, JsonOptions));
        command.Parameters.AddWithValue("$tool_call", item.IsToolInvocation ? 1 : 0);
        command.Parameters.AddWithValue("$user_text", (object?)UserText(item) ?? DBNull.Value);
        command.ExecuteNonQuery();

        if (item.Type == ItemType.UserMessage)
            attachmentStore.ReplaceItemReferences(connection, transaction, threadId, turnId, item, RemovedAttachmentPaths);
    }

    private static string? UserText(SessionItem item)
    {
        if (item.Type != ItemType.UserMessage)
            return null;
        var text = item.Payload switch
        {
            UserMessagePayload payload => payload.Text,
            JsonElement { ValueKind: JsonValueKind.Object } element
                when element.TryGetProperty("text", out var textElement)
                     && textElement.ValueKind == JsonValueKind.String => textElement.GetString(),
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private SessionTurn? LoadTurn(string turnId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT turn_json FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        return command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<SessionTurn>(json, JsonOptions)
            : null;
    }

    private long? LoadTurnOrdinal(string turnId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT rollout_ordinal FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$id", turnId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private long? LoadItemOrdinal(string turnId, string itemId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT rollout_ordinal FROM thread_items WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$id", itemId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private ThreadSummaryRuntime BuildPersistedRuntime(SessionThread thread)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT turn_json FROM thread_turns WHERE thread_id = $thread_id ORDER BY rollout_ordinal DESC LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        var lastTurn = command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<SessionTurn>(json, JsonOptions)
            : null;
        thread.Turns = lastTurn is null ? [] : [lastTurn];
        var runtime = ThreadSummaryRuntime.FromThread(thread);
        thread.Turns = [];
        return runtime;
    }
}
