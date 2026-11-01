using System.Diagnostics;
using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    private const int MessageSnippetContextLength = 80;

    public Task<ThreadMessageSearchPage> SearchMessageOccurrencesAsync(
        string threadId,
        string rolloutPath,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        bool repair,
        CancellationToken ct) =>
        ReadRetrievalAsync(
            threadId, rolloutPath, repair, "message_occurrences",
            () => QueryMessageOccurrences(threadId, searchTerm, cursor, limit, ct),
            static page => page.Data.Count, ct);

    public Task<IReadOnlyList<ThreadHistoryItem>> ReadItemsAsync(
        string threadId,
        string rolloutPath,
        string turnId,
        IReadOnlyList<string> itemIds,
        bool repair,
        CancellationToken ct) =>
        ReadRetrievalAsync(
            threadId, rolloutPath, repair, "exact_items",
            () => QueryExactItems(threadId, turnId, itemIds, ct),
            static items => items.Count, ct);

    private async Task<T> ReadRetrievalAsync<T>(
        string threadId,
        string rolloutPath,
        bool repair,
        string scope,
        Func<T> query,
        Func<T, int> rows,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        if (repair)
            await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        try
        {
            var result = query();
            QueryRows.Record(rows(result), new KeyValuePair<string, object?>("scope", scope));
            return result;
        }
        catch (JsonException) when (repair)
        {
            await RebuildCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
            return query();
        }
        finally
        {
            QueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", scope));
        }
    }

    private ThreadMessageSearchPage QueryMessageOccurrences(
        string threadId,
        string searchTerm,
        ThreadMessageSearchCursor? cursor,
        int limit,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.rollout_ordinal, i.turn_id, i.item_id, i.item_json, t.rollout_ordinal
            FROM thread_items i
            JOIN thread_turns t ON t.thread_id = i.thread_id AND t.turn_id = i.turn_id
            WHERE i.thread_id = $thread_id
              AND i.rollout_ordinal >= $ordinal
              AND CASE WHEN json_valid(i.item_json)
                       THEN json_extract(i.item_json, '$.type') IN ($user_type, $agent_type)
                       ELSE 1 END
            ORDER BY i.rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$ordinal", cursor?.ItemOrdinal ?? 0);
        command.Parameters.AddWithValue("$user_type", ItemType.UserMessage.ToString());
        command.Parameters.AddWithValue("$agent_type", ItemType.AgentMessage.ToString());
        using var reader = command.ExecuteReader();
        var hits = new List<ThreadMessageOccurrence>(limit);
        ThreadMessageSearchCursor? continuation = null;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var ordinal = reader.GetInt64(0);
            var item = DeserializeItem(reader.GetString(3));
            var text = item.Type switch
            {
                ItemType.UserMessage => item.AsUserMessage?.Text,
                ItemType.AgentMessage => item.AsAgentMessage?.Text,
                _ => null
            };
            if (text is null)
                continue;
            var offset = cursor is { } position && position.ItemOrdinal == ordinal ? position.TextOffset : 0;
            while (offset <= text.Length)
            {
                ct.ThrowIfCancellationRequested();
                var match = text.IndexOf(searchTerm, offset, StringComparison.OrdinalIgnoreCase);
                if (match < 0)
                    break;
                if (hits.Count == limit)
                    return new ThreadMessageSearchPage(hits, continuation);
                hits.Add(CreateMessageOccurrence(
                    reader.GetString(1), reader.GetString(2), text, match, searchTerm.Length, reader.GetInt64(4)));
                offset = match + searchTerm.Length;
                continuation = new ThreadMessageSearchCursor(ordinal, offset);
            }
        }
        return new ThreadMessageSearchPage(hits, null);
    }

    private static ThreadMessageOccurrence CreateMessageOccurrence(
        string turnId,
        string itemId,
        string text,
        int matchStart,
        int matchLength,
        long turnOrdinal)
    {
        var matchEnd = matchStart + matchLength;
        var start = Math.Max(0, matchStart - MessageSnippetContextLength);
        var end = matchEnd + Math.Min(MessageSnippetContextLength, text.Length - matchEnd);
        if (start > 0 && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1]))
            start--;
        if (end < text.Length && char.IsLowSurrogate(text[end]) && char.IsHighSurrogate(text[end - 1]))
            end++;
        return new ThreadMessageOccurrence(
            turnId, itemId, text[start..end], matchStart - start, matchEnd - start, turnOrdinal);
    }

    private IReadOnlyList<ThreadHistoryItem> QueryExactItems(
        string threadId,
        string turnId,
        IReadOnlyList<string> itemIds,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT turn_id, item_json FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id
              AND item_id IN ({string.Join(", ", itemIds.Select((_, index) => $"$item_{index}"))})
            ORDER BY rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        for (var index = 0; index < itemIds.Count; index++)
            command.Parameters.AddWithValue($"$item_{index}", itemIds[index]);
        using var reader = command.ExecuteReader();
        var items = new List<ThreadHistoryItem>(itemIds.Count);
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            items.Add(new ThreadHistoryItem(reader.GetString(0), DeserializeItem(reader.GetString(1))));
        }
        return items;
    }
}
