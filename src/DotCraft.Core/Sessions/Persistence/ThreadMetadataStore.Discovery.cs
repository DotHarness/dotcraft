using System.Text.Json;
using DotCraft.Persistence;

namespace DotCraft.Sessions;

internal sealed partial class ThreadMetadataStore
{
    public void UpsertDiscoveredSummary(ScannedThreadSummary scanned, string rolloutPath, long offset)
    {
        var summary = scanned.Summary;
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads (
                thread_id, rollout_path, user_id, origin_channel, channel_context, forked_from_id,
                ephemeral, worktree_json, git_info_json, source_json, display_name, status,
                created_at, updated_at, archived_at, history_mode, turn_count, metadata_json,
                projected_rollout_offset, attachment_index_version
            ) VALUES (
                $thread_id, $rollout_path, $user_id, $origin_channel, $channel_context, $forked_from_id,
                $ephemeral, $worktree_json, $git_info_json, $source_json, $display_name, $status,
                $created_at, $updated_at, $archived_at, $history_mode, $turn_count, $metadata_json,
                $offset, 0
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
                metadata_json = excluded.metadata_json,
                projected_rollout_offset = excluded.projected_rollout_offset
            """;
        command.Parameters.AddWithValue("$thread_id", summary.Id);
        command.Parameters.AddWithValue("$rollout_path", StoredRolloutPath.ToStored(connection, rolloutPath));
        command.Parameters.AddWithValue("$user_id", (object?)summary.UserId ?? DBNull.Value);
        command.Parameters.AddWithValue("$origin_channel", summary.OriginChannel);
        command.Parameters.AddWithValue("$channel_context", (object?)summary.ChannelContext ?? DBNull.Value);
        command.Parameters.AddWithValue("$forked_from_id", (object?)summary.ForkedFromId ?? DBNull.Value);
        command.Parameters.AddWithValue("$ephemeral", summary.Ephemeral ? 1 : 0);
        command.Parameters.AddWithValue("$worktree_json", summary.Worktree is null ? DBNull.Value : JsonSerializer.Serialize(summary.Worktree));
        command.Parameters.AddWithValue("$git_info_json", summary.GitInfo is null ? DBNull.Value : JsonSerializer.Serialize(summary.GitInfo));
        command.Parameters.AddWithValue("$source_json", JsonSerializer.Serialize(
            PersistedThreadSourceCodec.Encode(summary.Source), SessionPersistenceJsonOptions.Default));
        command.Parameters.AddWithValue("$display_name", (object?)summary.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", summary.Status.ToString());
        command.Parameters.AddWithValue("$created_at", summary.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", summary.LastActiveAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$archived_at", summary.Status == ThreadStatus.Archived
            ? summary.LastActiveAt.UtcDateTime.ToString("O") : DBNull.Value);
        command.Parameters.AddWithValue("$history_mode", scanned.HistoryMode.ToString());
        command.Parameters.AddWithValue("$turn_count", summary.TurnCount);
        command.Parameters.AddWithValue("$metadata_json", JsonSerializer.Serialize(summary.Metadata));
        command.Parameters.AddWithValue("$offset", offset);
        command.ExecuteNonQuery();
    }

    public bool AreAttachmentProjectionsCurrent()
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT NOT EXISTS (
                SELECT 1 FROM threads t
                LEFT JOIN thread_history_projection_state s ON s.thread_id = t.thread_id
                WHERE t.attachment_index_version != $version
                   OR s.thread_id IS NULL
                   OR s.rollout_path != t.rollout_path
                   OR s.projected_rollout_offset != t.projected_rollout_offset
            )
            """;
        command.Parameters.AddWithValue("$version", ThreadAttachmentStore.IndexVersion);
        return Convert.ToBoolean(command.ExecuteScalar());
    }
}
