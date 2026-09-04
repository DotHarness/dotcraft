using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed class ThreadHistoryProjectionStore(
    WorkspaceStateDatabase stateDatabase,
    ThreadMetadataStore metadataStore)
{
    private const int ReadBufferSize = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;
    private static readonly Meter Meter = new("DotCraft.ThreadHistory");
    private static readonly Counter<long> ProjectionEvents = Meter.CreateCounter<long>("dotcraft.thread_history.projection");
    private static readonly Counter<long> RepairBytes = Meter.CreateCounter<long>("dotcraft.thread_history.repair.bytes");
    private static readonly Counter<long> RepairRecords = Meter.CreateCounter<long>("dotcraft.thread_history.repair.records");
    private static readonly Histogram<double> QueryDuration = Meter.CreateHistogram<double>("dotcraft.thread_history.query.duration", "ms");
    private static readonly Histogram<long> QueryRows = Meter.CreateHistogram<long>("dotcraft.thread_history.query.rows");

    private static readonly HashSet<string> DomainKinds = new(StringComparer.Ordinal)
    {
        "thread_opened",
        "thread_name_updated",
        "thread_status_changed",
        "queued_input_added",
        "queued_input_removed",
        "queued_input_updated",
        "queued_input_reordered",
        "turn_started",
        "turn_completed",
        "item_appended",
        "turn_state_replaced",
        "thread_rolled_back"
    };

    private static readonly HashSet<string> IgnoredKinds = new(StringComparer.Ordinal)
    {
        "model_history_messages_appended",
        "context_compacted",
        "provider_history_items_appended",
        "provider_history_replaced",
        "provider_history_attempt_aborted"
    };

    public async Task ProjectCommittedAsync(
        string threadId,
        string rolloutPath,
        long confirmedOffset,
        CancellationToken ct)
    {
        await EnsureProjectionAsync(threadId, rolloutPath, confirmedOffset, forceRebuild: false, ct)
            .ConfigureAwait(false);
    }

    public async Task<ThreadHistorySnapshot> ReadSnapshotAsync(
        string threadId,
        string rolloutPath,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        try
        {
            var result = ReadSnapshot(threadId);
            QueryRows.Record(1, new KeyValuePair<string, object?>("scope", "snapshot"));
            return result;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            await RebuildCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
            return ReadSnapshot(threadId);
        }
        finally
        {
            QueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", "snapshot"));
        }
    }

    public async Task<ThreadHistoryPage<SessionTurn>> ListTurnsAsync(
        string threadId,
        string rolloutPath,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        try
        {
            var page = QueryTurns(threadId, cursor, limit, direction);
            QueryRows.Record(page.Data.Count, new KeyValuePair<string, object?>("scope", "turns"));
            return page;
        }
        catch (JsonException)
        {
            await RebuildCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
            return QueryTurns(threadId, cursor, limit, direction);
        }
        finally
        {
            QueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", "turns"));
        }
    }

    public async Task<ThreadHistoryPage<ThreadHistoryItem>> ListItemsAsync(
        string threadId,
        string rolloutPath,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        await EnsureCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
        try
        {
            var page = QueryItems(threadId, turnId, cursor, limit, direction);
            QueryRows.Record(page.Data.Count, new KeyValuePair<string, object?>("scope", turnId is null ? "items" : "turn_items"));
            return page;
        }
        catch (JsonException)
        {
            await RebuildCurrentAsync(threadId, rolloutPath, ct).ConfigureAwait(false);
            return QueryItems(threadId, turnId, cursor, limit, direction);
        }
        finally
        {
            QueryDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("scope", turnId is null ? "items" : "turn_items"));
        }
    }

    private async Task EnsureCurrentAsync(string threadId, string rolloutPath, CancellationToken ct)
    {
        var length = new FileInfo(rolloutPath).Length;
        await EnsureProjectionAsync(threadId, rolloutPath, length, forceRebuild: false, ct).ConfigureAwait(false);
    }

    private async Task RebuildCurrentAsync(string threadId, string rolloutPath, CancellationToken ct)
    {
        var length = new FileInfo(rolloutPath).Length;
        await EnsureProjectionAsync(threadId, rolloutPath, length, forceRebuild: true, ct).ConfigureAwait(false);
    }

    private async Task EnsureProjectionAsync(
        string threadId,
        string rolloutPath,
        long targetOffset,
        bool forceRebuild,
        CancellationToken ct)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(rolloutPath);
            var fileLength = new FileInfo(normalizedPath).Length;
            if (targetOffset < 0 || targetOffset > fileLength || !IsRecordBoundary(normalizedPath, targetOffset))
                throw new InvalidDataException("The history projection checkpoint is not a valid rollout record boundary.");

            using var connection = stateDatabase.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var state = LoadState(connection, transaction, threadId);
            var rebuild = forceRebuild || state is null;
            SessionThread? snapshot = null;
            var startOffset = 0L;
            var nextOrdinal = 1L;

            if (!rebuild && state is not null)
            {
                var pathMatches = string.Equals(state.RolloutPath, normalizedPath, StringComparison.OrdinalIgnoreCase);
                var archiveMove = !pathMatches && IsArchiveMove(state.RolloutPath, normalizedPath);
                rebuild = (!pathMatches && !archiveMove)
                    || state.ProjectedOffset < 0
                    || state.ProjectedOffset > targetOffset
                    || state.NextOrdinal <= 0
                    || !IsRecordBoundary(normalizedPath, state.ProjectedOffset);
                if (!rebuild)
                {
                    try
                    {
                        snapshot = JsonSerializer.Deserialize<SessionThread>(state.SnapshotJson, JsonOptions)
                            ?? throw new JsonException("Projected Thread snapshot is empty.");
                        startOffset = state.ProjectedOffset;
                        nextOrdinal = state.NextOrdinal;
                    }
                    catch (JsonException)
                    {
                        rebuild = true;
                        snapshot = null;
                        startOffset = 0;
                        nextOrdinal = 1;
                    }
                }
            }

            if (rebuild)
            {
                ClearProjection(connection, transaction, threadId);
                ProjectionEvents.Add(1, new KeyValuePair<string, object?>("outcome", "full_rebuild"));
            }
            else if (startOffset == targetOffset)
            {
                if (!string.Equals(state!.RolloutPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    UpdateProjectionPath(connection, transaction, threadId, normalizedPath);
                transaction.Commit();
                ProjectionEvents.Add(1, new KeyValuePair<string, object?>("outcome", "hit"));
                return;
            }
            else
            {
                ProjectionEvents.Add(1, new KeyValuePair<string, object?>("outcome", "incremental_repair"));
            }

            var projector = new ThreadHistoryProjector(
                connection,
                transaction,
                metadataStore,
                threadId,
                normalizedPath,
                snapshot,
                nextOrdinal);
            var recordCount = await ProjectLinesAsync(
                projector,
                normalizedPath,
                startOffset,
                targetOffset,
                ct).ConfigureAwait(false);
            projector.Publish(targetOffset);
            transaction.Commit();
            RepairBytes.Add(targetOffset - startOffset);
            RepairRecords.Add(recordCount);
        }
        catch (ThreadHistoryUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or SqliteException or InvalidOperationException)
        {
            ProjectionEvents.Add(1,
                new KeyValuePair<string, object?>("outcome", "failure"),
                new KeyValuePair<string, object?>("reason", ex.GetType().Name));
            throw new ThreadHistoryUnavailableException(
                $"Persisted history for Thread '{threadId}' is unavailable.", ex);
        }
    }

    private static async Task<long> ProjectLinesAsync(
        ThreadHistoryProjector projector,
        string path,
        long startOffset,
        long endOffset,
        CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            ReadBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        stream.Position = startOffset;
        var remaining = endOffset - startOffset;
        var buffer = new byte[ReadBufferSize];
        using var lineBuffer = new MemoryStream();
        long records = 0;

        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("The rollout ended before the confirmed projection offset.");
            remaining -= read;

            var segmentStart = 0;
            for (var index = 0; index < read; index++)
            {
                if (buffer[index] != (byte)'\n')
                    continue;
                lineBuffer.Write(buffer, segmentStart, index - segmentStart);
                ApplyLine(projector, lineBuffer);
                records++;
                lineBuffer.SetLength(0);
                segmentStart = index + 1;
            }
            if (segmentStart < read)
                lineBuffer.Write(buffer, segmentStart, read - segmentStart);
        }

        if (lineBuffer.Length != 0)
            throw new InvalidDataException("The confirmed projection offset ends inside a rollout record.");
        return records;
    }

    private static void ApplyLine(ThreadHistoryProjector projector, MemoryStream lineBuffer)
    {
        if (lineBuffer.Length == 0)
            return;
        var line = lineBuffer.GetBuffer().AsSpan(0, checked((int)lineBuffer.Length));
        var kind = RolloutJsonEnvelopeReader.ReadKind(line);
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new InvalidDataException("A rollout record has no kind envelope.");
        }

        if (DomainKinds.Contains(kind))
        {
            var record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions)
                ?? throw new InvalidDataException("The rollout record is empty.");
            projector.Apply(record);
        }
        else if (!IgnoredKinds.Contains(kind))
            throw new InvalidDataException($"Unsupported domain rollout record kind '{kind}'.");
    }

    private ThreadHistorySnapshot ReadSnapshot(string threadId)
    {
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT thread_snapshot_json, persisted_runtime_json FROM thread_history_projection_state WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Thread history projection state is missing.");
        var thread = JsonSerializer.Deserialize<SessionThread>(reader.GetString(0), JsonOptions)
            ?? throw new JsonException("Projected Thread snapshot is empty.");
        var runtime = JsonSerializer.Deserialize<ThreadSummaryRuntime>(reader.GetString(1), JsonOptions)
            ?? throw new JsonException("Projected Thread runtime is empty.");
        thread.Turns = [];
        return new ThreadHistorySnapshot(thread, runtime);
    }

    private ThreadHistoryPage<SessionTurn> QueryTurns(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction)
    {
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        var comparison = direction == ThreadHistorySortDirection.Ascending ? ">" : "<";
        var order = direction == ThreadHistorySortDirection.Ascending ? "ASC" : "DESC";
        command.CommandText = $"""
            SELECT rollout_ordinal, turn_json FROM thread_turns
            WHERE thread_id = $thread_id
              AND ($has_cursor = 0 OR rollout_ordinal {comparison} $cursor)
            ORDER BY rollout_ordinal {order}
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$has_cursor", cursor.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("$cursor", cursor?.ExclusiveRolloutOrdinal ?? 0);
        command.Parameters.AddWithValue("$limit", limit + 1);
        using var reader = command.ExecuteReader();
        var rows = new List<(long Ordinal, SessionTurn Turn)>(limit + 1);
        while (reader.Read())
        {
            var turn = JsonSerializer.Deserialize<SessionTurn>(reader.GetString(1), JsonOptions)
                ?? throw new JsonException("Projected Turn is empty.");
            turn.Items = [];
            rows.Add((reader.GetInt64(0), turn));
        }
        return BuildPage(rows, limit, static row => row.Turn, static row => row.Ordinal);
    }

    private ThreadHistoryPage<ThreadHistoryItem> QueryItems(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction)
    {
        using var connection = stateDatabase.OpenConnection();
        using var command = connection.CreateCommand();
        var comparison = direction == ThreadHistorySortDirection.Ascending ? ">" : "<";
        var order = direction == ThreadHistorySortDirection.Ascending ? "ASC" : "DESC";
        command.CommandText = $"""
            SELECT rollout_ordinal, turn_id, item_json FROM thread_items
            WHERE thread_id = $thread_id
              AND ($turn_id IS NULL OR turn_id = $turn_id)
              AND ($has_cursor = 0 OR rollout_ordinal {comparison} $cursor)
            ORDER BY rollout_ordinal {order}
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$turn_id", (object?)turnId ?? DBNull.Value);
        command.Parameters.AddWithValue("$has_cursor", cursor.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("$cursor", cursor?.ExclusiveRolloutOrdinal ?? 0);
        command.Parameters.AddWithValue("$limit", limit + 1);
        using var reader = command.ExecuteReader();
        var rows = new List<(long Ordinal, ThreadHistoryItem Item)>(limit + 1);
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<SessionItem>(reader.GetString(2), JsonOptions)
                ?? throw new JsonException("Projected Item is empty.");
            rows.Add((reader.GetInt64(0), new ThreadHistoryItem(reader.GetString(1), item)));
        }
        return BuildPage(rows, limit, static row => row.Item, static row => row.Ordinal);
    }

    private static ThreadHistoryPage<T> BuildPage<TRow, T>(
        List<TRow> rows,
        int limit,
        Func<TRow, T> select,
        Func<TRow, long> ordinal)
    {
        var hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);
        var data = rows.Select(select).ToList();
        ThreadHistoryCursor? next = hasMore && rows.Count > 0
            ? new ThreadHistoryCursor(ordinal(rows[^1]))
            : null;
        return new ThreadHistoryPage<T>(data, next);
    }

    private static ProjectionState? LoadState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT rollout_path, projected_rollout_offset, next_rollout_ordinal, thread_snapshot_json FROM thread_history_projection_state WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new ProjectionState(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3))
            : null;
    }

    private static void ClearProjection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId)
    {
        foreach (var table in new[] { "thread_history_projection_state", "thread_turns" })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE thread_id = $thread_id";
            command.Parameters.AddWithValue("$thread_id", threadId);
            command.ExecuteNonQuery();
        }
    }

    private static void UpdateProjectionPath(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string threadId,
        string path)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE thread_history_projection_state SET rollout_path = $path WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$path", path);
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    private static bool IsRecordBoundary(string path, long offset)
    {
        if (offset == 0)
            return true;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (offset > stream.Length)
            return false;
        stream.Position = offset - 1;
        return stream.ReadByte() == (byte)'\n';
    }

    private static bool IsArchiveMove(string previousPath, string currentPath)
    {
        if (!string.Equals(Path.GetFileName(previousPath), Path.GetFileName(currentPath), StringComparison.OrdinalIgnoreCase))
            return false;
        var previousParent = Path.GetFileName(Path.GetDirectoryName(previousPath));
        var currentParent = Path.GetFileName(Path.GetDirectoryName(currentPath));
        return (string.Equals(previousParent, "active", StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentParent, "archived", StringComparison.OrdinalIgnoreCase))
            || (string.Equals(previousParent, "archived", StringComparison.OrdinalIgnoreCase)
                && string.Equals(currentParent, "active", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ProjectionState(
        string RolloutPath,
        long ProjectedOffset,
        long NextOrdinal,
        string SnapshotJson);
}
