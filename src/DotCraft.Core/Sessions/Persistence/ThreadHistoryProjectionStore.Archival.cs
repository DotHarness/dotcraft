using System.Text.Json;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadHistoryProjectionStore
{
    public void ProjectArchive(string threadId, string previousPath, long previousOffset, RolloutAppendResult result)
    {
        try
        {
            using var connection = stateDatabase.OpenConnection();
            using var transaction = connection.BeginTransaction();
            var offset = result.Receipt.ConfirmedOffset;
            var path = StoredRolloutPath.ToStored(connection, result.Path);

            var state = LoadState(connection, transaction, threadId);
            var snapshot = state is not null
                && state.IndexVersion == ThreadAttachmentStore.IndexVersion
                && state.ProjectedOffset == previousOffset
                && state.ThreadRowOffset == previousOffset
                && state.NextOrdinal > 0
                && state.RolloutPath == previousPath
                && state.ThreadRowPath == previousPath
                && IsRecordBoundary(result.Path, previousOffset)
                ? ReadArchiveSnapshot(state.SnapshotJson, threadId)
                : null;

            if (snapshot is not null)
            {
                snapshot.Status = ThreadStatus.Archived;
                using var history = connection.CreateCommand();
                history.Transaction = transaction;
                history.CommandText = """
                    UPDATE thread_history_projection_state
                    SET rollout_path = $path, projected_rollout_offset = $offset, thread_snapshot_json = $snapshot
                    WHERE thread_id = $id
                    """;
                history.Parameters.AddWithValue("$path", path);
                history.Parameters.AddWithValue("$offset", offset);
                history.Parameters.AddWithValue("$snapshot", JsonSerializer.Serialize(snapshot, JsonOptions));
                history.Parameters.AddWithValue("$id", threadId);
                history.ExecuteNonQuery();
            }

            using var summary = connection.CreateCommand();
            summary.Transaction = transaction;
            summary.CommandText = """
                UPDATE threads
                SET rollout_path = $path, status = $archived, archived_at = updated_at,
                    projected_rollout_offset = CASE
                        WHEN rollout_path = $previous_path AND projected_rollout_offset = $previous_offset THEN $offset
                        ELSE projected_rollout_offset
                    END
                WHERE thread_id = $id
                """;
            summary.Parameters.AddWithValue("$path", path);
            summary.Parameters.AddWithValue("$archived", nameof(ThreadStatus.Archived));
            summary.Parameters.AddWithValue("$previous_path", StoredRolloutPath.ToStored(connection, previousPath));
            summary.Parameters.AddWithValue("$previous_offset", previousOffset);
            summary.Parameters.AddWithValue("$offset", offset);
            summary.Parameters.AddWithValue("$id", threadId);
            summary.ExecuteNonQuery();
            transaction.Commit();
            if (snapshot is not null)
                Interlocked.Add(ref _projectedBytes, offset - previousOffset);
        }
        catch (Exception ex) when (ex is SqliteException or IOException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning($"Archive projection update failed after rollout commit for '{threadId}': {ex.Message}");
        }
    }

    private static SessionThread? ReadArchiveSnapshot(string json, string threadId)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<SessionThread>(json, JsonOptions);
            return snapshot?.Id == threadId ? snapshot : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
