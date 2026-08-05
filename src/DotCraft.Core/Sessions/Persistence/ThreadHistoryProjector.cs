using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed class ThreadHistoryProjector(
    SqliteConnection connection,
    SqliteTransaction transaction,
    ThreadMetadataStore metadataStore,
    string threadId,
    string rolloutPath,
    SessionThread? snapshot,
    long nextOrdinal)
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public SessionThread? Snapshot { get; private set; } = snapshot;

    public long NextOrdinal { get; private set; } = nextOrdinal;

    public void Apply(string kind, string line)
    {
        switch (kind)
        {
            case "thread_opened":
                ApplyThreadOpened(Deserialize(line).ThreadOpened
                    ?? throw InvalidRecord(kind));
                break;
            case "thread_name_updated":
                EnsureSnapshot().DisplayName = Deserialize(line).ThreadNameUpdated?.DisplayName;
                break;
            case "thread_status_changed":
                ApplyStatus(Deserialize(line).ThreadStatusChanged
                    ?? throw InvalidRecord(kind));
                break;
            case "queued_input_added":
                ApplyQueueAdded(Deserialize(line).QueuedInputAdded
                    ?? throw InvalidRecord(kind));
                break;
            case "queued_input_removed":
                ApplyQueueRemoved(Deserialize(line).QueuedInputRemoved
                    ?? throw InvalidRecord(kind));
                break;
            case "queued_input_updated":
                ApplyQueueUpdated(Deserialize(line).QueuedInputUpdated
                    ?? throw InvalidRecord(kind));
                break;
            case "queued_input_reordered":
                ApplyQueueReordered(Deserialize(line).QueuedInputReordered
                    ?? throw InvalidRecord(kind));
                break;
            case "turn_started":
                ApplyTurnStarted(Deserialize(line).TurnStarted?.Turn
                    ?? throw InvalidRecord(kind));
                break;
            case "turn_completed":
                ApplyTurnCompleted(Deserialize(line).TurnCompleted
                    ?? throw InvalidRecord(kind));
                break;
            case "item_appended":
                ApplyItem(Deserialize(line).ItemAppended
                    ?? throw InvalidRecord(kind));
                break;
            case "turn_state_replaced":
                ApplyTurnReplacement(Deserialize(line).TurnStateReplaced
                    ?? throw InvalidRecord(kind));
                break;
            case "thread_rolled_back":
                ApplyRollback(Deserialize(line).ThreadRolledBack
                    ?? throw InvalidRecord(kind));
                break;
        }
    }

    public void Publish(long projectedOffset)
    {
        var thread = EnsureSnapshot();
        thread.Turns = [];

        var runtime = BuildPersistedRuntime(thread);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_history_projection_state (
                thread_id, rollout_path, projected_rollout_offset, next_rollout_ordinal,
                thread_snapshot_json, persisted_runtime_json
            ) VALUES (
                $thread_id, $rollout_path, $projected_offset, $next_ordinal,
                $snapshot_json, $runtime_json
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                rollout_path = excluded.rollout_path,
                projected_rollout_offset = excluded.projected_rollout_offset,
                next_rollout_ordinal = excluded.next_rollout_ordinal,
                thread_snapshot_json = excluded.thread_snapshot_json,
                persisted_runtime_json = excluded.persisted_runtime_json
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$rollout_path", rolloutPath);
        command.Parameters.AddWithValue("$projected_offset", projectedOffset);
        command.Parameters.AddWithValue("$next_ordinal", NextOrdinal);
        command.Parameters.AddWithValue("$snapshot_json", JsonSerializer.Serialize(thread, JsonOptions));
        command.Parameters.AddWithValue("$runtime_json", JsonSerializer.Serialize(runtime, JsonOptions));
        command.ExecuteNonQuery();

        using var countCommand = connection.CreateCommand();
        countCommand.Transaction = transaction;
        countCommand.CommandText = """
            UPDATE threads
            SET turn_count = (SELECT COUNT(*) FROM thread_turns WHERE thread_id = $thread_id)
            WHERE thread_id = $thread_id
            """;
        countCommand.Parameters.AddWithValue("$thread_id", threadId);
        countCommand.ExecuteNonQuery();
    }

    private void ApplyThreadOpened(ThreadOpenedPayload opened)
    {
        if (!string.Equals(opened.ThreadId, threadId, StringComparison.Ordinal))
            throw new InvalidDataException("Thread history record belongs to another Thread.");

        var thread = Snapshot ?? new SessionThread();
        thread.Id = opened.ThreadId;
        thread.WorkspacePath = opened.WorkspacePath;
        thread.UserId = opened.UserId;
        thread.OriginChannel = opened.OriginChannel;
        thread.ChannelContext = opened.ChannelContext;
        thread.Source = PersistedThreadSourceCodec.Decode(
            opened.Source ?? throw new InvalidDataException("The canonical Thread header has no source."));
        thread.ForkedFromId = opened.ForkedFromId;
        thread.Ephemeral = opened.Ephemeral;
        thread.Worktree = opened.Worktree;
        thread.CreatedAt = opened.CreatedAt;
        thread.LastActiveAt = opened.LastActiveAt;
        thread.Metadata = new Dictionary<string, string>(opened.Metadata);
        thread.HistoryMode = opened.HistoryMode;
        thread.Configuration = opened.Configuration;
        thread.ProviderHistorySchemaVersion = opened.ProviderHistorySchemaVersion;
        thread.Turns = [];
        Snapshot = thread;

        // Establish the FK parent only when state.db was rebuilt. Existing metadata projection
        // owns its independent checkpoint and must not be advanced by history publication.
        using var parentCheck = connection.CreateCommand();
        parentCheck.Transaction = transaction;
        parentCheck.CommandText = "SELECT 1 FROM threads WHERE thread_id = $thread_id";
        parentCheck.Parameters.AddWithValue("$thread_id", threadId);
        if (parentCheck.ExecuteScalar() is null)
            metadataStore.UpsertThread(connection, transaction, thread, rolloutPath, 0);
    }

    private void ApplyStatus(RolloutThreadStatusChangedPayload status)
    {
        var thread = EnsureSnapshot();
        thread.Status = status.Status;
        thread.LastActiveAt = status.LastActiveAt;
    }

    private void ApplyQueueAdded(QueuedInputAddedPayload payload)
    {
        var queue = EnsureSnapshot().QueuedInputs;
        if (queue.All(item => !string.Equals(item.Id, payload.QueuedInput.Id, StringComparison.Ordinal)))
            queue.Add(payload.QueuedInput);
    }

    private void ApplyQueueRemoved(QueuedInputRemovedPayload payload)
    {
        var thread = EnsureSnapshot();
        thread.QueuedInputs.RemoveAll(item => string.Equals(item.Id, payload.QueuedInputId, StringComparison.Ordinal));
        thread.LastActiveAt = payload.LastActiveAt;
    }

    private void ApplyQueueUpdated(QueuedInputUpdatedPayload payload)
    {
        var thread = EnsureSnapshot();
        var index = thread.QueuedInputs.FindIndex(item =>
            string.Equals(item.Id, payload.QueuedInput.Id, StringComparison.Ordinal));
        if (index >= 0)
            thread.QueuedInputs[index] = payload.QueuedInput;
        thread.LastActiveAt = payload.LastActiveAt;
    }

    private void ApplyQueueReordered(QueuedInputReorderedPayload payload)
    {
        var thread = EnsureSnapshot();
        var byId = thread.QueuedInputs.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reordered = new List<QueuedTurnInput>(thread.QueuedInputs.Count);
        foreach (var id in payload.OrderedQueuedInputIds)
        {
            if (seen.Add(id) && byId.TryGetValue(id, out var queued))
                reordered.Add(queued);
        }
        reordered.AddRange(thread.QueuedInputs.Where(item => !seen.Contains(item.Id)));
        thread.QueuedInputs = reordered;
        thread.LastActiveAt = payload.LastActiveAt;
    }

    private void ApplyTurnStarted(SessionTurn turn)
    {
        EnsureSnapshot();
        turn.ThreadId = threadId;
        turn.Items = [];
        turn.Input = null;
        UpsertTurn(turn, preserveOrdinal: true);
    }

    private void ApplyTurnCompleted(TurnCompletedPayload payload)
    {
        var turn = LoadTurn(payload.TurnId)
            ?? throw new InvalidDataException($"Turn '{payload.TurnId}' completed before it was projected.");
        turn.Status = payload.Status;
        turn.CompletedAt = payload.CompletedAt;
        turn.TokenUsage = payload.TokenUsage;
        turn.Error = payload.Error;
        turn.OriginChannel = payload.OriginChannel;
        turn.Initiator = payload.Initiator;
        UpsertTurn(turn, preserveOrdinal: true);
    }

    private void ApplyItem(ItemAppendedPayload payload)
    {
        var existingTurn = LoadTurn(payload.TurnId);
        var turn = existingTurn ?? new SessionTurn
        {
            Id = payload.TurnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = payload.Item.CreatedAt,
            Items = []
        };
        if (existingTurn is null)
            UpsertTurn(turn, preserveOrdinal: false);

        if (turn.Input is null && payload.Item.Type == ItemType.UserMessage)
        {
            turn.Input = payload.Item;
            UpsertTurn(turn, preserveOrdinal: true);
        }
        UpsertItem(payload.TurnId, payload.Item);
    }

    private void ApplyTurnReplacement(TurnStateReplacedPayload payload)
    {
        var thread = EnsureSnapshot();
        var replacement = payload.Turn;
        replacement.ThreadId = threadId;
        var items = replacement.Items.ToList();
        replacement.Items = [];
        UpsertTurn(replacement, preserveOrdinal: true);

        var retained = new HashSet<string>(items.Select(item => item.Id), StringComparer.Ordinal);
        foreach (var item in items)
            UpsertItem(replacement.Id, item);

        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = retained.Count == 0
            ? "DELETE FROM thread_items WHERE thread_id = $thread_id AND turn_id = $turn_id"
            : $"DELETE FROM thread_items WHERE thread_id = $thread_id AND turn_id = $turn_id AND item_id NOT IN ({string.Join(",", retained.Select((_, index) => $"$item_{index}"))})";
        delete.Parameters.AddWithValue("$thread_id", threadId);
        delete.Parameters.AddWithValue("$turn_id", replacement.Id);
        var parameterIndex = 0;
        foreach (var id in retained)
            delete.Parameters.AddWithValue($"$item_{parameterIndex++}", id);
        delete.ExecuteNonQuery();

        thread.Status = payload.ThreadStatus;
        thread.LastActiveAt = payload.LastActiveAt;
        thread.DisplayName = payload.DisplayName;
    }

    private void ApplyRollback(ThreadRolledBackPayload payload)
    {
        if (payload.NumTurns <= 0)
            throw new InvalidDataException("Rollback count must be positive.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM thread_turns
            WHERE thread_id = $thread_id AND turn_id IN (
                SELECT turn_id FROM thread_turns
                WHERE thread_id = $thread_id
                ORDER BY rollout_ordinal DESC
                LIMIT $count
            )
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$count", payload.NumTurns);
        command.ExecuteNonQuery();
        EnsureSnapshot().LastActiveAt = payload.LastActiveAt;
    }

    private void UpsertTurn(SessionTurn turn, bool preserveOrdinal)
    {
        var ordinal = preserveOrdinal ? LoadTurnOrdinal(turn.Id) : null;
        ordinal ??= NextOrdinal++;
        turn.Items = [];
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_turns (thread_id, turn_id, rollout_ordinal, turn_json)
            VALUES ($thread_id, $turn_id, $ordinal, $json)
            ON CONFLICT(thread_id, turn_id) DO UPDATE SET turn_json = excluded.turn_json
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turn.Id);
        command.Parameters.AddWithValue("$ordinal", ordinal.Value);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(turn, JsonOptions));
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
                thread_id, turn_id, item_id, rollout_ordinal, updated_rollout_ordinal, item_json
            ) VALUES ($thread_id, $turn_id, $item_id, $ordinal, $updated, $json)
            ON CONFLICT(thread_id, turn_id, item_id) DO UPDATE SET
                updated_rollout_ordinal = excluded.updated_rollout_ordinal,
                item_json = excluded.item_json
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", turnId);
        command.Parameters.AddWithValue("$item_id", item.Id);
        command.Parameters.AddWithValue("$ordinal", position);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(item, JsonOptions));
        command.ExecuteNonQuery();
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

    private long? LoadTurnOrdinal(string turnId) => LoadOrdinal(
        "SELECT rollout_ordinal FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $id",
        turnId);

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

    private long? LoadOrdinal(string sql, string id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$id", id);
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

    private SessionThread EnsureSnapshot() => Snapshot
        ?? throw new InvalidDataException("The rollout does not begin with a canonical Thread header.");

    private static ThreadRolloutRecord Deserialize(string line) =>
        JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions)
        ?? throw new InvalidDataException("The rollout record is empty.");

    private static InvalidDataException InvalidRecord(string kind) =>
        new($"The '{kind}' rollout record is incomplete.");
}
