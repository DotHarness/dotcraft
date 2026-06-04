using System.Text.Json;
using DotCraft.Context.Compaction;
using DotCraft.State;

namespace DotCraft.Protocol;

internal sealed class ThreadMetadataStore(StateRuntime stateRuntime)
{
    public void UpsertThread(SessionThread thread, string rolloutPath)
    {
        var summary = ThreadSummary.FromThread(thread);
        var firstUserMessage = ExtractFirstUserMessage(thread);
        var archivedAt = thread.Status == ThreadStatus.Archived ? thread.LastActiveAt : (DateTimeOffset?)null;

        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO threads (
                thread_id,
                rollout_path,
                workspace_path,
                user_id,
                origin_channel,
                channel_context,
                forked_from_id,
                ephemeral,
                worktree_json,
                display_name,
                status,
                created_at,
                updated_at,
                archived_at,
                history_mode,
                turn_count,
                first_user_message,
                metadata_json
            ) VALUES (
                $thread_id,
                $rollout_path,
                $workspace_path,
                $user_id,
                $origin_channel,
                $channel_context,
                $forked_from_id,
                $ephemeral,
                $worktree_json,
                $display_name,
                $status,
                $created_at,
                $updated_at,
                $archived_at,
                $history_mode,
                $turn_count,
                $first_user_message,
                $metadata_json
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                rollout_path = excluded.rollout_path,
                workspace_path = excluded.workspace_path,
                user_id = excluded.user_id,
                origin_channel = excluded.origin_channel,
                channel_context = excluded.channel_context,
                forked_from_id = excluded.forked_from_id,
                ephemeral = excluded.ephemeral,
                worktree_json = excluded.worktree_json,
                display_name = excluded.display_name,
                status = excluded.status,
                created_at = excluded.created_at,
                updated_at = excluded.updated_at,
                archived_at = excluded.archived_at,
                history_mode = excluded.history_mode,
                turn_count = excluded.turn_count,
                first_user_message = excluded.first_user_message,
                metadata_json = excluded.metadata_json
            """;
        command.Parameters.AddWithValue("$thread_id", thread.Id);
        command.Parameters.AddWithValue("$rollout_path", rolloutPath);
        command.Parameters.AddWithValue("$workspace_path", summary.WorkspacePath);
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
        command.Parameters.AddWithValue("$display_name", (object?)summary.DisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", summary.Status.ToString());
        command.Parameters.AddWithValue("$created_at", summary.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", summary.LastActiveAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$archived_at", archivedAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$history_mode", thread.HistoryMode.ToString());
        command.Parameters.AddWithValue("$turn_count", summary.TurnCount);
        command.Parameters.AddWithValue("$first_user_message", (object?)firstUserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$metadata_json", JsonSerializer.Serialize(summary.Metadata));
        command.ExecuteNonQuery();
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
                workspace_path,
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
                worktree_json
            FROM threads
            ORDER BY updated_at DESC, thread_id DESC
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var originChannel = reader.GetString(3);
            list.Add(new ThreadSummary
            {
                Id = reader.GetString(0),
                UserId = reader.IsDBNull(1) ? null : reader.GetString(1),
                WorkspacePath = reader.GetString(2),
                OriginChannel = originChannel,
                ChannelContext = reader.IsDBNull(4) ? null : reader.GetString(4),
                DisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Source = string.Equals(originChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase)
                    ? ThreadSource.ForSubAgent(new SubAgentThreadSource())
                    : ThreadSource.User(),
                Status = Enum.TryParse<ThreadStatus>(reader.GetString(6), out var status) ? status : ThreadStatus.Active,
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7)),
                LastActiveAt = DateTimeOffset.Parse(reader.GetString(8)),
                TurnCount = reader.GetInt32(9),
                Metadata = reader.IsDBNull(10)
                    ? []
                    : ParseMetadata(reader.GetString(10)),
                ForkedFromId = reader.IsDBNull(11) ? null : reader.GetString(11),
                Ephemeral = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                Worktree = reader.IsDBNull(13) ? null : ParseWorktree(reader.GetString(13))
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
        return command.ExecuteScalar() as string;
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
                reader.GetString(1),
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
        command.Parameters.AddWithValue("$rollout_path", rolloutPath);
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

    public void UpsertThreadSpawnEdge(ThreadSpawnEdge edge)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_spawn_edges (
                parent_thread_id,
                child_thread_id,
                parent_turn_id,
                depth,
                agent_path,
                task_name,
                agent_nickname,
                agent_role,
                profile_name,
                runtime_type,
                supports_send_input,
                supports_resume,
                supports_send_message,
                supports_followup_task,
                supports_close,
                status,
                created_at,
                updated_at
            ) VALUES (
                $parent_thread_id,
                $child_thread_id,
                $parent_turn_id,
                $depth,
                $agent_path,
                $task_name,
                $agent_nickname,
                $agent_role,
                $profile_name,
                $runtime_type,
                $supports_send_input,
                $supports_resume,
                $supports_send_message,
                $supports_followup_task,
                $supports_close,
                $status,
                $created_at,
                $updated_at
            )
            ON CONFLICT(parent_thread_id, child_thread_id) DO UPDATE SET
                parent_turn_id = excluded.parent_turn_id,
                depth = excluded.depth,
                agent_path = excluded.agent_path,
                task_name = excluded.task_name,
                agent_nickname = excluded.agent_nickname,
                agent_role = excluded.agent_role,
                profile_name = excluded.profile_name,
                runtime_type = excluded.runtime_type,
                supports_send_input = excluded.supports_send_input,
                supports_resume = excluded.supports_resume,
                supports_send_message = excluded.supports_send_message,
                supports_followup_task = excluded.supports_followup_task,
                supports_close = excluded.supports_close,
                status = excluded.status,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$parent_thread_id", edge.ParentThreadId);
        command.Parameters.AddWithValue("$child_thread_id", edge.ChildThreadId);
        command.Parameters.AddWithValue("$parent_turn_id", (object?)edge.ParentTurnId ?? DBNull.Value);
        command.Parameters.AddWithValue("$depth", edge.Depth);
        command.Parameters.AddWithValue("$agent_path", (object?)edge.AgentPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$task_name", (object?)edge.TaskName ?? DBNull.Value);
        command.Parameters.AddWithValue("$agent_nickname", (object?)edge.AgentNickname ?? DBNull.Value);
        command.Parameters.AddWithValue("$agent_role", (object?)edge.AgentRole ?? DBNull.Value);
        command.Parameters.AddWithValue("$profile_name", (object?)edge.ProfileName ?? DBNull.Value);
        command.Parameters.AddWithValue("$runtime_type", (object?)edge.RuntimeType ?? DBNull.Value);
        command.Parameters.AddWithValue("$supports_send_input", edge.SupportsSendInput ? 1 : 0);
        command.Parameters.AddWithValue("$supports_resume", edge.SupportsResume ? 1 : 0);
        command.Parameters.AddWithValue("$supports_send_message", edge.SupportsSendMessage ? 1 : 0);
        command.Parameters.AddWithValue("$supports_followup_task", edge.SupportsFollowupTask ? 1 : 0);
        command.Parameters.AddWithValue("$supports_close", edge.SupportsClose ? 1 : 0);
        command.Parameters.AddWithValue("$status", edge.Status);
        command.Parameters.AddWithValue("$created_at", edge.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", edge.UpdatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void SetThreadSpawnEdgeStatus(string parentThreadId, string childThreadId, string status)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE thread_spawn_edges
            SET status = $status, updated_at = $updated_at
            WHERE parent_thread_id = $parent_thread_id AND child_thread_id = $child_thread_id
            """;
        command.Parameters.AddWithValue("$parent_thread_id", parentThreadId);
        command.Parameters.AddWithValue("$child_thread_id", childThreadId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public List<ThreadSpawnEdge> ListSubAgentChildren(string parentThreadId, bool includeClosed)
    {
        var edges = new List<ThreadSpawnEdge>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = includeClosed
            ? """
              SELECT parent_thread_id, child_thread_id, parent_turn_id, depth, agent_path, task_name, agent_nickname, agent_role, profile_name, runtime_type, supports_send_input, supports_resume, supports_send_message, supports_followup_task, supports_close, status, created_at, updated_at
              FROM thread_spawn_edges
              WHERE parent_thread_id = $parent_thread_id
              ORDER BY updated_at DESC, child_thread_id DESC
              """
            : """
              SELECT parent_thread_id, child_thread_id, parent_turn_id, depth, agent_path, task_name, agent_nickname, agent_role, profile_name, runtime_type, supports_send_input, supports_resume, supports_send_message, supports_followup_task, supports_close, status, created_at, updated_at
              FROM thread_spawn_edges
              WHERE parent_thread_id = $parent_thread_id AND status <> $closed
              ORDER BY updated_at DESC, child_thread_id DESC
              """;
        command.Parameters.AddWithValue("$parent_thread_id", parentThreadId);
        if (!includeClosed)
            command.Parameters.AddWithValue("$closed", ThreadSpawnEdgeStatus.Closed);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            edges.Add(new ThreadSpawnEdge
            {
                ParentThreadId = reader.GetString(0),
                ChildThreadId = reader.GetString(1),
                ParentTurnId = reader.IsDBNull(2) ? null : reader.GetString(2),
                Depth = reader.GetInt32(3),
                AgentPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                TaskName = reader.IsDBNull(5) ? null : reader.GetString(5),
                AgentNickname = reader.IsDBNull(6) ? null : reader.GetString(6),
                AgentRole = reader.IsDBNull(7) ? null : reader.GetString(7),
                ProfileName = reader.IsDBNull(8) ? null : reader.GetString(8),
                RuntimeType = reader.IsDBNull(9) ? null : reader.GetString(9),
                SupportsSendInput = !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
                SupportsResume = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
                SupportsSendMessage = !reader.IsDBNull(12) && reader.GetInt32(12) != 0,
                SupportsFollowupTask = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
                SupportsClose = reader.IsDBNull(14) || reader.GetInt32(14) != 0,
                Status = reader.GetString(15),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(16)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(17))
            });
        }

        return edges;
    }

    public void AddSubAgentMailboxEntry(SubAgentMailboxEntry entry)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO subagent_mailbox_entries (
                id,
                root_thread_id,
                sender_agent_path,
                target_agent_path,
                message,
                status,
                created_at,
                delivered_at
            ) VALUES (
                $id,
                $root_thread_id,
                $sender_agent_path,
                $target_agent_path,
                $message,
                $status,
                $created_at,
                $delivered_at
            )
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$root_thread_id", entry.RootThreadId);
        command.Parameters.AddWithValue("$sender_agent_path", entry.SenderAgentPath);
        command.Parameters.AddWithValue("$target_agent_path", entry.TargetAgentPath);
        command.Parameters.AddWithValue("$message", entry.Message);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$delivered_at", entry.DeliveredAt?.UtcDateTime.ToString("O") ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    public List<SubAgentMailboxEntry> ListPendingSubAgentMailbox(string rootThreadId, string targetAgentPath)
    {
        var entries = new List<SubAgentMailboxEntry>();
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, root_thread_id, sender_agent_path, target_agent_path, message, status, created_at, delivered_at
            FROM subagent_mailbox_entries
            WHERE root_thread_id = $root_thread_id
              AND target_agent_path = $target_agent_path
              AND status = $status
            ORDER BY created_at ASC, id ASC
            """;
        command.Parameters.AddWithValue("$root_thread_id", rootThreadId);
        command.Parameters.AddWithValue("$target_agent_path", targetAgentPath);
        command.Parameters.AddWithValue("$status", SubAgentMailboxStatus.Pending);
        using var reader = command.ExecuteReader();
        while (reader.Read())
            entries.Add(ReadMailboxEntry(reader));

        return entries;
    }

    public void MarkSubAgentMailboxDelivered(string rootThreadId, IReadOnlyList<string> entryIds, DateTimeOffset deliveredAt)
    {
        if (entryIds.Count == 0)
            return;

        using var connection = stateRuntime.OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var entryId in entryIds)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                UPDATE subagent_mailbox_entries
                SET status = $status, delivered_at = $delivered_at
                WHERE root_thread_id = $root_thread_id
                  AND id = $id
                  AND status = $pending
                """;
            command.Parameters.AddWithValue("$status", SubAgentMailboxStatus.Delivered);
            command.Parameters.AddWithValue("$delivered_at", deliveredAt.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$root_thread_id", rootThreadId);
            command.Parameters.AddWithValue("$id", entryId);
            command.Parameters.AddWithValue("$pending", SubAgentMailboxStatus.Pending);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static SubAgentMailboxEntry ReadMailboxEntry(Microsoft.Data.Sqlite.SqliteDataReader reader) =>
        new()
        {
            Id = reader.GetString(0),
            RootThreadId = reader.GetString(1),
            SenderAgentPath = reader.GetString(2),
            TargetAgentPath = reader.GetString(3),
            Message = reader.GetString(4),
            Status = reader.GetString(5),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(6)),
            DeliveredAt = reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7))
        };

    public void SaveSessionJson(string threadId, string sessionJson)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_sessions(thread_id, session_json, updated_at)
            VALUES ($thread_id, $session_json, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                session_json = excluded.session_json,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$session_json", sessionJson);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public string? LoadSessionJson(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_json FROM thread_sessions WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() as string;
    }

    public bool SessionExists(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM thread_sessions WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() != null;
    }

    public void DeleteSession(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_sessions WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    public ThreadGoal? LoadThreadGoal(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT thread_id, goal_id, objective, status, token_budget,
                   input_tokens, output_tokens, cached_input_tokens,
                   cache_write_input_tokens, reasoning_output_tokens, total_tokens,
                   time_used_seconds, created_at_utc, updated_at_utc
            FROM thread_goals
            WHERE thread_id = $thread_id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadGoal(reader) : null;
    }

    public void UpsertThreadGoal(ThreadGoal goal)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_goals (
                thread_id, goal_id, objective, status, token_budget,
                input_tokens, output_tokens, cached_input_tokens,
                cache_write_input_tokens, reasoning_output_tokens, total_tokens,
                time_used_seconds, created_at_utc, updated_at_utc
            ) VALUES (
                $thread_id, $goal_id, $objective, $status, $token_budget,
                $input_tokens, $output_tokens, $cached_input_tokens,
                $cache_write_input_tokens, $reasoning_output_tokens, $total_tokens,
                $time_used_seconds, $created_at_utc, $updated_at_utc
            )
            ON CONFLICT(thread_id) DO UPDATE SET
                goal_id = excluded.goal_id,
                objective = excluded.objective,
                status = excluded.status,
                token_budget = excluded.token_budget,
                input_tokens = excluded.input_tokens,
                output_tokens = excluded.output_tokens,
                cached_input_tokens = excluded.cached_input_tokens,
                cache_write_input_tokens = excluded.cache_write_input_tokens,
                reasoning_output_tokens = excluded.reasoning_output_tokens,
                total_tokens = excluded.total_tokens,
                time_used_seconds = excluded.time_used_seconds,
                created_at_utc = excluded.created_at_utc,
                updated_at_utc = excluded.updated_at_utc
            """;
        command.Parameters.AddWithValue("$thread_id", goal.ThreadId);
        command.Parameters.AddWithValue("$goal_id", goal.GoalId);
        command.Parameters.AddWithValue("$objective", goal.Objective);
        command.Parameters.AddWithValue("$status", ToGoalStatusStorage(goal.Status));
        command.Parameters.AddWithValue("$token_budget", goal.TokenBudget.HasValue ? goal.TokenBudget.Value : DBNull.Value);
        command.Parameters.AddWithValue("$input_tokens", goal.TokensUsed.InputTokens);
        command.Parameters.AddWithValue("$output_tokens", goal.TokensUsed.OutputTokens);
        command.Parameters.AddWithValue("$cached_input_tokens", goal.TokensUsed.CachedInputTokens);
        command.Parameters.AddWithValue("$cache_write_input_tokens", goal.TokensUsed.CacheWriteInputTokens);
        command.Parameters.AddWithValue("$reasoning_output_tokens", goal.TokensUsed.ReasoningOutputTokens);
        command.Parameters.AddWithValue("$total_tokens", goal.TokensUsed.TotalTokens);
        command.Parameters.AddWithValue("$time_used_seconds", goal.TimeUsedSeconds);
        command.Parameters.AddWithValue("$created_at_utc", goal.CreatedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$updated_at_utc", goal.UpdatedAt.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public ThreadGoal? AccountThreadGoalUsage(
        string threadId,
        string expectedGoalId,
        TokenUsageInfo usageDelta,
        long timeDeltaSeconds)
    {
        using var connection = stateRuntime.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = """
            SELECT thread_id, goal_id, objective, status, token_budget,
                   input_tokens, output_tokens, cached_input_tokens,
                   cache_write_input_tokens, reasoning_output_tokens, total_tokens,
                   time_used_seconds, created_at_utc, updated_at_utc
            FROM thread_goals
            WHERE thread_id = $thread_id AND goal_id = $goal_id
            LIMIT 1
            """;
        select.Parameters.AddWithValue("$thread_id", threadId);
        select.Parameters.AddWithValue("$goal_id", expectedGoalId);

        ThreadGoal? current;
        using (var reader = select.ExecuteReader())
        {
            current = reader.Read() ? ReadGoal(reader) : null;
        }

        if (current == null)
        {
            transaction.Commit();
            return null;
        }

        var nextTokens = current.TokensUsed + usageDelta;
        var nextStatus = current.Status == ThreadGoalStatus.Active
            && current.TokenBudget.HasValue
            && nextTokens.TotalTokens >= current.TokenBudget.Value
                ? ThreadGoalStatus.BudgetLimited
                : current.Status;
        var next = current with
        {
            TokensUsed = nextTokens,
            TimeUsedSeconds = current.TimeUsedSeconds + Math.Max(0, timeDeltaSeconds),
            Status = nextStatus,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE thread_goals
            SET status = $status,
                input_tokens = $input_tokens,
                output_tokens = $output_tokens,
                cached_input_tokens = $cached_input_tokens,
                cache_write_input_tokens = $cache_write_input_tokens,
                reasoning_output_tokens = $reasoning_output_tokens,
                total_tokens = $total_tokens,
                time_used_seconds = $time_used_seconds,
                updated_at_utc = $updated_at_utc
            WHERE thread_id = $thread_id AND goal_id = $goal_id
            """;
        update.Parameters.AddWithValue("$thread_id", threadId);
        update.Parameters.AddWithValue("$goal_id", expectedGoalId);
        update.Parameters.AddWithValue("$status", ToGoalStatusStorage(next.Status));
        update.Parameters.AddWithValue("$input_tokens", next.TokensUsed.InputTokens);
        update.Parameters.AddWithValue("$output_tokens", next.TokensUsed.OutputTokens);
        update.Parameters.AddWithValue("$cached_input_tokens", next.TokensUsed.CachedInputTokens);
        update.Parameters.AddWithValue("$cache_write_input_tokens", next.TokensUsed.CacheWriteInputTokens);
        update.Parameters.AddWithValue("$reasoning_output_tokens", next.TokensUsed.ReasoningOutputTokens);
        update.Parameters.AddWithValue("$total_tokens", next.TokensUsed.TotalTokens);
        update.Parameters.AddWithValue("$time_used_seconds", next.TimeUsedSeconds);
        update.Parameters.AddWithValue("$updated_at_utc", next.UpdatedAt.UtcDateTime.ToString("O"));
        update.ExecuteNonQuery();
        transaction.Commit();
        return next;
    }

    public bool DeleteThreadGoal(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_goals WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteNonQuery() > 0;
    }

    public long? LoadContextUsageTokens(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT context_usage_tokens FROM thread_context_usage WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    public ContextUsageAnchor? LoadContextUsageAnchor(string threadId)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT context_usage_tokens, message_count, prefix_fingerprint
            FROM thread_context_usage
            WHERE thread_id = $thread_id
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(1) || reader.IsDBNull(2))
            return null;

        var fingerprint = reader.GetString(2);
        if (string.IsNullOrWhiteSpace(fingerprint))
            return null;

        return new ContextUsageAnchor(
            Tokens: reader.GetInt64(0),
            MessageCount: reader.GetInt32(1),
            PrefixFingerprint: fingerprint);
    }

    public void SaveContextUsageTokens(string threadId, long tokens)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_context_usage(thread_id, context_usage_tokens, message_count, prefix_fingerprint, updated_at)
            VALUES ($thread_id, $tokens, NULL, NULL, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                context_usage_tokens = excluded.context_usage_tokens,
                message_count = NULL,
                prefix_fingerprint = NULL,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$tokens", Math.Max(0, tokens));
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void SaveContextUsageAnchor(string threadId, ContextUsageAnchor anchor)
    {
        using var connection = stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_context_usage(thread_id, context_usage_tokens, message_count, prefix_fingerprint, updated_at)
            VALUES ($thread_id, $tokens, $message_count, $prefix_fingerprint, $updated_at)
            ON CONFLICT(thread_id) DO UPDATE SET
                context_usage_tokens = excluded.context_usage_tokens,
                message_count = excluded.message_count,
                prefix_fingerprint = excluded.prefix_fingerprint,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$tokens", Math.Max(0, anchor.Tokens));
        command.Parameters.AddWithValue("$message_count", Math.Max(0, anchor.MessageCount));
        command.Parameters.AddWithValue("$prefix_fingerprint", (object?)anchor.PrefixFingerprint ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static string? ExtractFirstUserMessage(SessionThread thread)
    {
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type != ItemType.UserMessage)
                    continue;

                if (item.Payload is UserMessagePayload payload && !string.IsNullOrWhiteSpace(payload.Text))
                    return payload.Text.Trim();

                if (item.Payload is JsonElement element
                    && element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }
        }

        return null;
    }

    private static ThreadGoal ReadGoal(Microsoft.Data.Sqlite.SqliteDataReader reader)
    {
        var tokens = new TokenUsageInfo
        {
            InputTokens = reader.GetInt64(5),
            OutputTokens = reader.GetInt64(6),
            CachedInputTokens = reader.GetInt64(7),
            CacheWriteInputTokens = reader.GetInt64(8),
            ReasoningOutputTokens = reader.GetInt64(9),
            TotalTokens = reader.GetInt64(10)
        };

        return new ThreadGoal
        {
            ThreadId = reader.GetString(0),
            GoalId = reader.GetString(1),
            Objective = reader.GetString(2),
            Status = FromGoalStatusStorage(reader.GetString(3)),
            TokenBudget = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            TokensUsed = tokens,
            TimeUsedSeconds = reader.GetInt64(11),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(12)),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(13))
        };
    }

    private static string ToGoalStatusStorage(ThreadGoalStatus status) => status switch
    {
        ThreadGoalStatus.Active => "active",
        ThreadGoalStatus.Paused => "paused",
        ThreadGoalStatus.BudgetLimited => "budget_limited",
        ThreadGoalStatus.Complete => "complete",
        _ => "active"
    };

    private static ThreadGoalStatus FromGoalStatusStorage(string status) => status switch
    {
        "active" => ThreadGoalStatus.Active,
        "paused" => ThreadGoalStatus.Paused,
        "budget_limited" => ThreadGoalStatus.BudgetLimited,
        "complete" => ThreadGoalStatus.Complete,
        _ => ThreadGoalStatus.Active
    };
}

internal sealed record ThreadRolloutLocation(string ThreadId, string RolloutPath, ThreadStatus Status);
