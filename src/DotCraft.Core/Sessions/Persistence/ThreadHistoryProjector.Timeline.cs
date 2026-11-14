using System.Text.Json;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjector
{
    private void SetCompletionOrdinal(SessionTurn turn, long ordinal)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE thread_turns
            SET completed_rollout_ordinal = CASE WHEN $terminal = 1
                THEN COALESCE(completed_rollout_ordinal, $ordinal) ELSE NULL END
            WHERE thread_id = $thread_id AND turn_id = $turn_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turn.Id);
        command.Parameters.AddWithValue("$ordinal", ordinal);
        command.Parameters.AddWithValue("$terminal",
            turn.Status is TurnStatus.Completed or TurnStatus.Failed or TurnStatus.Cancelled ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void PreserveHistoricalPhase(string turnId, SessionItem item)
    {
        if (item.Payload is not AgentMessagePayload { Phase: null } message)
            return;
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT agent_phase FROM thread_items
            WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $item_id
            """;
        read.Parameters.AddWithValue("$thread_id", threadId);
        read.Parameters.AddWithValue("$turn_id", turnId);
        read.Parameters.AddWithValue("$item_id", item.Id);
        if (read.ExecuteScalar() is string phase)
            item.Payload = message with { Phase = phase };
    }

    private void ApplyHistoricalPhases(SessionTurn turn)
    {
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = """
                SELECT 1 FROM thread_items
                WHERE thread_id = $thread_id AND turn_id = $turn_id
                  AND item_type = $agent_type AND agent_phase IS NULL
                LIMIT 1
                """;
            check.Parameters.AddWithValue("$thread_id", threadId);
            check.Parameters.AddWithValue("$turn_id", turn.Id);
            check.Parameters.AddWithValue("$agent_type", (int)ItemType.AgentMessage);
            if (check.ExecuteScalar() is null)
                return;
        }

        var missing = new List<SessionItem>();
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT item_id, item_type,
                       CASE WHEN item_type = $agent_type THEN item_json ELSE NULL END
                FROM thread_items
                WHERE thread_id = $thread_id AND turn_id = $turn_id
                ORDER BY rollout_ordinal
                """;
            read.Parameters.AddWithValue("$thread_id", threadId);
            read.Parameters.AddWithValue("$turn_id", turn.Id);
            read.Parameters.AddWithValue("$agent_type", (int)ItemType.AgentMessage);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var item = reader.IsDBNull(2)
                    ? new SessionItem { Id = reader.GetString(0), TurnId = turn.Id, Type = (ItemType)reader.GetInt32(1) }
                    : JsonSerializer.Deserialize<SessionItem>(reader.GetString(2), JsonOptions)
                        ?? throw new JsonException("Projected Agent Item is empty.");
                turn.Items.Add(item);
                if (item.AsAgentMessage is { Phase: null })
                    missing.Add(item);
            }
        }
        AgentMessagePhases.ApplyHistoricalTurn(turn);
        turn.Items = [];
        foreach (var item in missing)
        {
            if (item.AsAgentMessage?.Phase is not { } phase)
                continue;
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE thread_items SET item_json = $json, agent_phase = $phase
                WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id = $item_id
                  AND agent_phase IS NULL
                """;
            update.Parameters.AddWithValue("$thread_id", threadId);
            update.Parameters.AddWithValue("$turn_id", turn.Id);
            update.Parameters.AddWithValue("$item_id", item.Id);
            update.Parameters.AddWithValue("$json", JsonSerializer.Serialize(item, JsonOptions));
            update.Parameters.AddWithValue("$phase", phase);
            update.ExecuteNonQuery();
        }
    }
}
