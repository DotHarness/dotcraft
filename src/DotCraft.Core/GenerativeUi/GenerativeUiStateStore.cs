using DotCraft.Persistence;

namespace DotCraft.GenerativeUi;

/// <summary>The saved view state of one Generative UI block, as serialized JSON.</summary>
public sealed record GenerativeUiBlockState(string TurnId, string ItemId, int BlockIndex, string StateJson);

/// <summary>Stores Generative UI view state per thread, turn, item, and block.</summary>
public sealed class GenerativeUiStateStore(WorkspaceStateDatabase database)
{
    public IReadOnlyList<GenerativeUiBlockState> Load(string threadId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT turn_id, item_id, block_index, state_json
            FROM generative_ui_state
            WHERE thread_id = $thread_id
            ORDER BY turn_id, item_id, block_index
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        var result = new List<GenerativeUiBlockState>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new GenerativeUiBlockState(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
        return result;
    }

    /// <summary>Replaces a block's state; a <c>null</c> state removes it.</summary>
    public void Save(string threadId, string turnId, string itemId, int blockIndex, string? stateJson)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$item_id", itemId);
        command.Parameters.AddWithValue("$block_index", blockIndex);
        if (stateJson is null)
        {
            command.CommandText = """
                DELETE FROM generative_ui_state
                WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $item_id AND block_index = $block_index
                """;
            command.ExecuteNonQuery();
            return;
        }

        command.CommandText = """
            INSERT INTO generative_ui_state(thread_id, turn_id, item_id, block_index, state_json, updated_at)
            VALUES ($thread_id, $turn_id, $item_id, $block_index, $state_json, $updated_at)
            ON CONFLICT(thread_id, turn_id, item_id, block_index) DO UPDATE SET
                state_json = excluded.state_json,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$state_json", stateJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }
}
