using System.Text.Json;
using DotCraft.Persistence;
using Microsoft.Data.Sqlite;

namespace DotCraft.Sessions;

internal sealed partial class ThreadMetadataStore
{
    public static void UpsertThread(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionThread thread,
        string rolloutPath,
        long projectedRolloutOffset,
        int attachmentIndexVersion)
    {
        var summary = ThreadSummary.FromThread(thread);
        var archivedAt = thread.Status == ThreadStatus.Archived ? thread.LastActiveAt : (DateTimeOffset?)null;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO threads (
                thread_id,
                rollout_path,
                user_id,
                origin_channel,
                channel_context,
                forked_from_id,
                ephemeral,
                worktree_json,
                git_info_json,
                source_json,
                display_name,
                status,
                created_at,
                updated_at,
                archived_at,
                history_mode,
                turn_count,
                first_user_message,
                metadata_json,
                projected_rollout_offset,
                attachment_index_version
            ) VALUES (
                $thread_id,
                $rollout_path,
                $user_id,
                $origin_channel,
                $channel_context,
                $forked_from_id,
                $ephemeral,
                $worktree_json,
                $git_info_json,
                $source_json,
                $display_name,
                $status,
                $created_at,
                $updated_at,
                $archived_at,
                $history_mode,
                (SELECT COUNT(*) FROM thread_turns WHERE thread_id = $thread_id),
                (SELECT i.user_text
                 FROM thread_items i
                 JOIN thread_turns t ON t.thread_id = i.thread_id AND t.turn_id = i.turn_id
                 WHERE i.thread_id = $thread_id AND i.user_text IS NOT NULL
                 ORDER BY t.rollout_ordinal, i.rollout_ordinal
                 LIMIT 1),
                $metadata_json,
                $projected_rollout_offset,
                $attachment_index_version
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                rollout_path = excluded.rollout_path,
                user_id = excluded.user_id,
                origin_channel = excluded.origin_channel,
                channel_context = excluded.channel_context,
                forked_from_id = excluded.forked_from_id,
                ephemeral = excluded.ephemeral,
                worktree_json = excluded.worktree_json,
                git_info_json = excluded.git_info_json,
                source_json = excluded.source_json,
                display_name = excluded.display_name,
                status = excluded.status,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                archived_at = excluded.archived_at,
                history_mode = excluded.history_mode,
                turn_count = excluded.turn_count,
                first_user_message = excluded.first_user_message,
                metadata_json = excluded.metadata_json,
                projected_rollout_offset = excluded.projected_rollout_offset,
                attachment_index_version = excluded.attachment_index_version
            """;
        command.Parameters.AddWithValue("$thread_id", thread.Id);
        command.Parameters.AddWithValue("$rollout_path", StoredRolloutPath.ToStored(connection, rolloutPath));
        command.Parameters.AddWithValue("$user_id", (object?)summary.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin_channel", summary.OriginChannel);
        command.Parameters.AddWithValue("$channel_context", (object?)summary.ChannelContext ?? DBNull.Value);
        command.Parameters.AddWithValue("$forked_from_id", (object?)summary.ForkedFromId ?? DBNull.Value);
        command.Parameters.AddWithValue("$ephemeral", summary.Ephemeral ? 1 : 0);
        command.Parameters.AddWithValue(
            "$worktree_json",
            summary.Worktree == null
                ? DBNull.Value
                : JsonSerializer.Serialize(summary.Worktree));
        command.Parameters.AddWithValue(
            "$git_info_json",
            summary.GitInfo == null
                ? DBNull.Value
                : JsonSerializer.Serialize(summary.GitInfo));
        command.Parameters.AddWithValue(
            "$source_json",
            JsonSerializer.Serialize(
                PersistedThreadSourceCodec.Encode(summary.Source),
                SessionPersistenceJsonOptions.Default));
        command.Parameters.AddWithValue("$display_name", (object?)summary.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", summary.Status.ToString());
        command.Parameters.AddWithValue("$created_at", summary.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", summary.LastActiveAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$archived_at", archivedAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$history_mode", thread.HistoryMode.ToString());
        command.Parameters.AddWithValue("$metadata_json", JsonSerializer.Serialize(summary.Metadata));
        command.Parameters.AddWithValue("$projected_rollout_offset", projectedRolloutOffset);
        command.Parameters.AddWithValue("$attachment_index_version", attachmentIndexVersion);
        command.ExecuteNonQuery();
    }

    public Dictionary<string, ThreadProjectionState> LoadProjectionStates()
    {
        var states = new Dictionary<string, ThreadProjectionState>(StringComparer.Ordinal);
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT thread_id, rollout_path, projected_rollout_offset, attachment_index_version FROM threads";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            states[reader.GetString(0)] = new ThreadProjectionState(
                StoredRolloutPath.Resolve(connection, reader.GetString(1)),
                reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
                reader.GetInt32(3));
        }
        return states;
    }

    public List<ThreadSummary> LoadIndex()
    {
        var list = new List<ThreadSummary>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                thread_id,
                user_id,
                origin_channel,
                channel_context,
                display_name,
                status,
                created_at,
                updated_at,
                turn_count,
                metadata_json,
                forked_from_id,
                ephemeral,
                worktree_json,
                source_json,
                git_info_json
            FROM threads
            ORDER BY updated_at DESC, thread_id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var originChannel = reader.GetString(2);
            var metadata = reader.IsDBNull(9)
                ? new Dictionary<string, string>()
                : ParseMetadata(reader.GetString(9));
            list.Add(new ThreadSummary
            {
                Id = reader.GetString(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetString(1),
                WorkspacePath = stateRuntime.WorkspacePath,
                OriginChannel = originChannel,
                ChannelContext = reader.IsDBNull(3) ? null : reader.GetString(3),
                DisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Source = reader.IsDBNull(13)
                    ? throw new InvalidDataException($"Thread '{reader.GetString(0)}' has no source projection.")
                    : PersistedThreadSourceCodec.Decode(
                        JsonSerializer.Deserialize<PersistedThreadSource>(
                            reader.GetString(13),
                            SessionPersistenceJsonOptions.Default)
                        ?? throw new JsonException("Thread source projection decoded to null.")),
                Status = Enum.TryParse<ThreadStatus>(reader.GetString(5), out var status) ? status : ThreadStatus.Active,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(7)),
                TurnCount = reader.GetInt32(8),
                Metadata = metadata,
                ForkedFromId = reader.IsDBNull(10) ? null : reader.GetString(10),
                Ephemeral = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
                Worktree = reader.IsDBNull(12) ? null : ParseWorktree(reader.GetString(12)),
                GitInfo = reader.IsDBNull(14) ? null : JsonSerializer.Deserialize<ThreadGitInfo>(reader.GetString(14))
            });
        }

        return list;
    }

    private static ThreadWorktreeInfo? ParseWorktree(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ThreadWorktreeInfo>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseMetadata(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public string? GetRolloutPath(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rollout_path FROM threads WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() is string stored ? StoredRolloutPath.Resolve(connection, stored) : null;
    }

    public List<ThreadRolloutLocation> LoadRolloutLocations()
    {
        var list = new List<ThreadRolloutLocation>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, rollout_path, status
            FROM threads
            ORDER BY updated_at DESC, thread_id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ThreadRolloutLocation(
                reader.GetString(0),
                StoredRolloutPath.Resolve(connection, reader.GetString(1)),
                Enum.TryParse<ThreadStatus>(reader.GetString(2), out var status) ? status : ThreadStatus.Active));
        }

        return list;
    }

    public void UpdateRolloutPath(string threadId, string rolloutPath)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE threads
            SET rollout_path = $rollout_path
            WHERE thread_id = $thread_id
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$rollout_path", StoredRolloutPath.ToStored(connection, rolloutPath));
        command.ExecuteNonQuery();
    }

    public void DeleteThread(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM threads WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }
}

internal sealed record ThreadProjectionState(string RolloutPath, long ProjectedRolloutOffset, int AttachmentIndexVersion);
