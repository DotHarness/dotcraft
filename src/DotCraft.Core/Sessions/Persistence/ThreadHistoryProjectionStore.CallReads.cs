namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    public Task<IReadOnlyList<ThreadHistoryItem>> ReadCallItemsAsync(
        string threadId, string rolloutPath, string turnId, IReadOnlyList<string> callIds,
        bool repair, CancellationToken ct) =>
        ReadRetrievalAsync(threadId, rolloutPath, repair, "call_items",
            () => QueryCallItems(threadId, turnId, callIds, ct), static items => items.Count, ct);

    private IReadOnlyList<ThreadHistoryItem> QueryCallItems(
        string threadId, string turnId, IReadOnlyList<string> callIds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT turn_id, item_json FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id
              AND summary_call_id IN ({string.Join(", ", callIds.Select((_, index) => $"$call_{index}"))})
            ORDER BY rollout_ordinal
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        for (var index = 0; index < callIds.Count; index++)
            command.Parameters.AddWithValue($"$call_{index}", callIds[index]);
        using var reader = command.ExecuteReader();
        var items = new List<ThreadHistoryItem>();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            items.Add(new ThreadHistoryItem(reader.GetString(0), DeserializeItem(reader.GetString(1))));
        }
        return items;
    }
}
