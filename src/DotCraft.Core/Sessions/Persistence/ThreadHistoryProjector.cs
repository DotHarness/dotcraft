using System.Text.Json;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjector(
    SqliteConnection connection,
    SqliteTransaction transaction,
    ThreadAttachmentStore attachmentStore,
    string threadId,
    string rolloutPath,
    SessionThread? snapshot,
    long nextOrdinal)
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    private bool _queueChanged;

    public SessionThread? Snapshot { get; private set; } = snapshot;

    public long NextOrdinal { get; private set; } = nextOrdinal;

    public HashSet<string> RemovedAttachmentPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Apply(ThreadRolloutRecord record)
    {
        var kind = record.Kind;
        switch (kind)
        {
            case RolloutKinds.ThreadOpened:
                ApplyThreadOpened(record.ThreadOpened
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.ThreadNameUpdated:
                EnsureSnapshot().DisplayName = record.ThreadNameUpdated?.DisplayName;
                break;
            case RolloutKinds.ThreadStatusChanged:
                ApplyStatus(record.ThreadStatusChanged
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.QueuedInputAdded:
                ApplyQueueAdded(record.QueuedInputAdded
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.QueuedInputRemoved:
                ApplyQueueRemoved(record.QueuedInputRemoved
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.QueuedInputUpdated:
                ApplyQueueUpdated(record.QueuedInputUpdated
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.QueuedInputReordered:
                ApplyQueueReordered(record.QueuedInputReordered
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.TurnStarted:
                ApplyTurnStarted(record.TurnStarted?.Turn
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.TurnCompleted:
                ApplyTurnCompleted(record.TurnCompleted
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.ItemAppended:
                ApplyItem(record.ItemAppended
                    ?? throw InvalidRecord(kind), record.Timestamp);
                break;
            case RolloutKinds.TurnStateReplaced:
                ApplyTurnReplacement(record.TurnStateReplaced
                    ?? throw InvalidRecord(kind));
                break;
            case RolloutKinds.ThreadRolledBack:
                ApplyRollback(record.ThreadRolledBack
                    ?? throw InvalidRecord(kind));
                break;
        }
    }

    public void Publish(long projectedOffset)
    {
        var thread = EnsureSnapshot();
        thread.Turns = [];
        thread.WorkspacePath = string.Empty;

        if (_queueChanged)
            attachmentStore.ReplaceQueuedReferences(connection, transaction, threadId, thread.QueuedInputs, RemovedAttachmentPaths);
        ThreadMetadataStore.UpsertThread(
            connection,
            transaction,
            thread,
            rolloutPath,
            projectedOffset,
            ThreadAttachmentStore.IndexVersion);

        var runtime = BuildPersistedRuntime(thread);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO thread_history_projection_state (
                thread_id, rollout_path, projected_rollout_offset, next_rollout_ordinal,
                thread_snapshot_json, persisted_runtime_json,
                turn_sequence_high_watermark, provider_history_schema_version
            ) VALUES (
                $thread_id, $rollout_path, $projected_offset, $next_ordinal,
                $snapshot_json, $runtime_json,
                $watermark, $provider_history_schema_version
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                rollout_path = excluded.rollout_path,
                projected_rollout_offset = excluded.projected_rollout_offset,
                next_rollout_ordinal = excluded.next_rollout_ordinal,
                thread_snapshot_json = excluded.thread_snapshot_json,
                persisted_runtime_json = excluded.persisted_runtime_json,
                turn_sequence_high_watermark = excluded.turn_sequence_high_watermark,
                provider_history_schema_version = excluded.provider_history_schema_version
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$rollout_path", StoredRolloutPath.ToStored(connection, rolloutPath));
        command.Parameters.AddWithValue("$projected_offset", projectedOffset);
        command.Parameters.AddWithValue("$next_ordinal", NextOrdinal);
        command.Parameters.AddWithValue("$snapshot_json", JsonSerializer.Serialize(thread, JsonOptions));
        command.Parameters.AddWithValue("$runtime_json", JsonSerializer.Serialize(runtime, JsonOptions));
        command.Parameters.AddWithValue("$watermark", thread.TurnSequenceHighWatermark);
        command.Parameters.AddWithValue("$provider_history_schema_version", thread.ProviderHistorySchemaVersion);
        command.ExecuteNonQuery();
    }

    private void ApplyThreadOpened(ThreadOpenedPayload opened)
    {
        if (!string.Equals(opened.ThreadId, threadId, StringComparison.Ordinal))
            throw new InvalidDataException("Thread history record belongs to another Thread.");

        var thread = Snapshot ?? new SessionThread();
        thread.Id = opened.ThreadId;
        thread.UserId = opened.UserId;
        thread.OriginChannel = opened.OriginChannel;
        thread.ChannelContext = opened.ChannelContext;
        thread.Source = PersistedThreadSourceCodec.Decode(
            opened.Source ?? throw new InvalidDataException("The canonical Thread header has no source."));
        thread.ForkedFromId = opened.ForkedFromId;
        thread.Ephemeral = opened.Ephemeral;
        thread.Worktree = opened.Worktree;
        thread.GitInfo = opened.GitInfo;
        thread.CreatedAt = opened.CreatedAt;
        thread.LastActiveAt = opened.LastActiveAt;
        thread.Metadata = new Dictionary<string, string>(opened.Metadata);
        thread.HistoryMode = opened.HistoryMode;
        thread.Configuration = opened.Configuration;
        thread.ProviderHistorySchemaVersion = opened.ProviderHistorySchemaVersion;
        thread.TurnSequenceHighWatermark = Math.Max(thread.TurnSequenceHighWatermark, opened.TurnSequenceHighWatermark);
        thread.Turns = [];
        Snapshot = thread;

        using var parentCheck = connection.CreateCommand();
        parentCheck.Transaction = transaction;
        parentCheck.CommandText = "SELECT 1 FROM threads WHERE thread_id = $thread_id";
        parentCheck.Parameters.AddWithValue("$thread_id", threadId);
        if (parentCheck.ExecuteScalar() is null)
            ThreadMetadataStore.UpsertThread(connection, transaction, thread, rolloutPath, 0, 0);
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
        _queueChanged = true;
    }

    private void ApplyQueueRemoved(QueuedInputRemovedPayload payload)
    {
        var thread = EnsureSnapshot();
        thread.QueuedInputs.RemoveAll(item => string.Equals(item.Id, payload.QueuedInputId, StringComparison.Ordinal));
        thread.LastActiveAt = payload.LastActiveAt;
        _queueChanged = true;
    }

    private void ApplyQueueUpdated(QueuedInputUpdatedPayload payload)
    {
        var thread = EnsureSnapshot();
        var index = thread.QueuedInputs.FindIndex(item =>
            string.Equals(item.Id, payload.QueuedInput.Id, StringComparison.Ordinal));
        if (index >= 0)
            thread.QueuedInputs[index] = payload.QueuedInput;
        thread.LastActiveAt = payload.LastActiveAt;
        _queueChanged = true;
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
        RaiseWatermark(turn.Id);
        turn.ThreadId = threadId;
        turn.Items = [];
        turn.Input = null;
        UpsertTurn(turn, preserveOrdinal: true);
    }

    private void ApplyTurnCompleted(TurnCompletedPayload payload)
    {
        if (LoadTurn(payload.TurnId) is not { } turn)
            return;
        turn.Status = payload.Status;
        turn.CompletedAt = payload.CompletedAt;
        turn.TokenUsage = payload.TokenUsage;
        turn.Error = payload.Error;
        turn.OriginChannel = payload.OriginChannel;
        turn.Initiator = payload.Initiator;
        UpsertTurn(turn, preserveOrdinal: true);
    }

    private void ApplyItem(ItemAppendedPayload payload, DateTimeOffset timestamp)
    {
        if (payload.Item is not { } item)
            return;

        var existingTurn = LoadTurn(payload.TurnId);
        var turn = existingTurn ?? new SessionTurn
        {
            Id = payload.TurnId,
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = timestamp,
            Items = []
        };
        if (existingTurn is null)
        {
            RaiseWatermark(turn.Id);
            UpsertTurn(turn, preserveOrdinal: false);
        }

        if (turn.Input is null && item.Type == ItemType.UserMessage)
        {
            turn.Input = item;
            UpsertTurn(turn, preserveOrdinal: true);
        }
        UpsertItem(payload.TurnId, item);
    }

    private void ApplyTurnReplacement(TurnStateReplacedPayload payload)
    {
        var thread = EnsureSnapshot();
        var replacement = payload.Turn;
        RaiseWatermark(replacement.Id);
        replacement.ThreadId = threadId;
        var items = replacement.Items.ToList();
        replacement.Input ??= items.FirstOrDefault(static item => item.Type == ItemType.UserMessage);
        replacement.Items = [];
        UpsertTurn(replacement, preserveOrdinal: true);

        var retained = new HashSet<string>(items.Select(item => item.Id), StringComparer.Ordinal);
        attachmentStore.DeleteTurnReferences(connection, transaction, threadId, replacement.Id, retained, RemovedAttachmentPaths);
        using (var delete = connection.CreateCommand())
        {
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
        }

        foreach (var item in items)
            UpsertItem(replacement.Id, item);

        thread.Status = payload.ThreadStatus;
        thread.LastActiveAt = payload.LastActiveAt;
        thread.DisplayName = payload.DisplayName;
    }

    private void ApplyRollback(ThreadRolledBackPayload payload)
    {
        var removed = new List<string>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = """
                SELECT turn_id FROM thread_turns
                WHERE thread_id = $thread_id
                ORDER BY rollout_ordinal DESC
                LIMIT $count
                """;
            select.Parameters.AddWithValue("$thread_id", threadId);
            select.Parameters.AddWithValue("$count", Math.Max(0, payload.NumTurns));
            using var reader = select.ExecuteReader();
            while (reader.Read())
                removed.Add(reader.GetString(0));
        }

        foreach (var turnId in removed)
        {
            attachmentStore.DeleteTurnReferences(connection, transaction, threadId, turnId, [], RemovedAttachmentPaths);
            using var delete = connection.CreateCommand();
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM thread_turns WHERE thread_id = $thread_id AND turn_id = $turn_id";
            delete.Parameters.AddWithValue("$thread_id", threadId);
            delete.Parameters.AddWithValue("$turn_id", turnId);
            delete.ExecuteNonQuery();
        }
        EnsureSnapshot().LastActiveAt = payload.LastActiveAt;
    }

    private void RaiseWatermark(string turnId)
    {
        var thread = EnsureSnapshot();
        thread.TurnSequenceHighWatermark = Math.Max(
            thread.TurnSequenceHighWatermark,
            SessionIdGenerator.LastTurnSequence([turnId]));
    }

    private SessionThread EnsureSnapshot() => Snapshot
        ?? throw new InvalidDataException("The rollout does not begin with a canonical Thread header.");

    private static InvalidDataException InvalidRecord(string kind) =>
        new($"The '{kind}' rollout record is incomplete.");
}
