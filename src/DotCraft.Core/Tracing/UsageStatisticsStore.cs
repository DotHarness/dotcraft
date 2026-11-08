using DotCraft.Persistence;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;

namespace DotCraft.Tracing;

public sealed partial class UsageStatisticsStore(
    UsageStatisticsDatabase database,
    string workspacePath,
    bool collectionEnabled = true)
{
    private readonly string _workspacePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
    private readonly string _workspaceKey = UsageStatisticsDatabase.WorkspaceKey(workspacePath);

    public void Record(TokenUsageRecord record)
    {
        if (!collectionEnabled)
            return;
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ThreadId);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.TurnId);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO usage_turns (
                workspace_key, thread_id, turn_id, timestamp, root_thread_id,
                surface, source_id, source_mode, subject_kind, subject_id, subject_label,
                context_kind, context_id, context_label, model, reasoning_effort, speed,
                llm_call_count, input_tokens, output_tokens, cached_input_tokens,
                cache_write_input_tokens, reasoning_output_tokens, cost_usd
            ) VALUES (
                $workspace, $thread, $turn, $timestamp, $root,
                COALESCE((SELECT origin_channel FROM usage_threads WHERE workspace_key = $workspace AND thread_id = $root), $source),
                $source, $mode, $subject_kind, $subject_id, $subject_label,
                $context_kind, $context_id, $context_label, $model, $reasoning, $speed,
                $calls, $input, $output, $cached, $cache_write, $reasoning_tokens, $cost
            )
            """;
        Add(command, "$workspace", _workspaceKey);
        Add(command, "$thread", record.ThreadId);
        Add(command, "$turn", record.TurnId);
        Add(command, "$timestamp", UsageDateRange.FormatTimestamp(record.Timestamp));
        Add(command, "$root", record.RootThreadId ?? record.ThreadId);
        Add(command, "$source", record.SourceId);
        Add(command, "$mode", record.SourceMode);
        Add(command, "$subject_kind", record.SubjectKind);
        Add(command, "$subject_id", record.SubjectId);
        Add(command, "$subject_label", record.SubjectKind == TokenUsageSubjectKinds.Thread ? null : record.SubjectLabel);
        Add(command, "$context_kind", record.ContextKind);
        Add(command, "$context_id", record.ContextId);
        Add(command, "$context_label", record.ContextLabel);
        Add(command, "$model", record.Model);
        Add(command, "$reasoning", record.ReasoningEffort);
        Add(command, "$speed", record.Speed);
        Add(command, "$calls", record.LlmCallCount);
        Add(command, "$input", record.InputTokens);
        Add(command, "$output", record.OutputTokens);
        Add(command, "$cached", record.CachedInputTokens);
        Add(command, "$cache_write", record.CacheWriteInputTokens);
        Add(command, "$reasoning_tokens", record.ReasoningOutputTokens);
        Add(command, "$cost", record.CostUsd is { } cost ? (double)cost : null);
        command.ExecuteNonQuery();
    }

    public void ObserveThread(SessionThread thread)
    {
        if (!collectionEnabled || thread.Ephemeral)
            return;
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        WriteThread(connection, thread, insert: true, transaction);
        if (!ThreadVisibility.IsInternal(ThreadSummary.FromThread(thread))
            && thread.Source.SubAgent is null
            && !string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO usage_events (workspace_key, event_id, timestamp, type, surface)
                VALUES ($workspace, $event, $timestamp, 'ThreadObserved', $surface)
                """;
            Add(command, "$workspace", _workspaceKey);
            Add(command, "$event", "thread:" + thread.Id);
            Add(command, "$timestamp", UsageDateRange.FormatTimestamp(DateTimeOffset.UtcNow));
            Add(command, "$surface", thread.OriginChannel);
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void UpdateThread(SessionThread thread)
    {
        if (thread.Ephemeral)
            return;
        using var connection = database.OpenConnection();
        WriteThread(connection, thread, insert: false);
    }

    public void ArchiveThread(string threadId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE usage_threads SET archived = 1 WHERE workspace_key = $workspace AND thread_id = $thread";
        Add(command, "$workspace", _workspaceKey);
        Add(command, "$thread", threadId);
        command.ExecuteNonQuery();
    }

    public void DeleteThread(string threadId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM usage_threads WHERE workspace_key = $workspace AND thread_id = $thread";
        Add(command, "$workspace", _workspaceKey);
        Add(command, "$thread", threadId);
        command.ExecuteNonQuery();
    }

    internal void RecordActivity(TraceEvent evt, string? rootThreadId)
    {
        if (!collectionEnabled || evt.Type is not (
            TraceEventType.Response or TraceEventType.ToolCallCompleted or TraceEventType.SkillReferenced
            or TraceEventType.Error or TraceEventType.ProviderError or TraceEventType.ContextCompaction
            or TraceEventType.TurnCompleted))
            return;
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO usage_events (
                workspace_key, event_id, timestamp, type, surface,
                model_id, reasoning_effort, tool_name, tool_source, duration_ms
            ) VALUES (
                $workspace, $event, $timestamp, $type,
                COALESCE((SELECT origin_channel FROM usage_threads WHERE workspace_key = $workspace AND thread_id = $root), 'unknown'),
                $model, $reasoning, $tool, $source, $duration
            )
            """;
        Add(command, "$workspace", _workspaceKey);
        Add(command, "$event", evt.Id);
        Add(command, "$timestamp", UsageDateRange.FormatTimestamp(evt.Timestamp));
        Add(command, "$type", evt.Type.ToString());
        Add(command, "$root", rootThreadId);
        Add(command, "$model", evt.Type == TraceEventType.Response ? evt.ModelId : null);
        Add(command, "$reasoning", evt.Type == TraceEventType.Response ? evt.ReasoningEffort : null);
        Add(command, "$tool", evt.ToolName);
        Add(command, "$source", evt.ToolSource);
        Add(command, "$duration", evt.DurationMs);
        command.ExecuteNonQuery();
    }

    private void WriteThread(SqliteConnection connection, SessionThread thread, bool insert, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert
            ? """
              INSERT INTO usage_threads (workspace_key, workspace_path, thread_id, root_thread_id, title, origin_channel, archived)
              VALUES ($workspace, $path, $thread, $root, $title, $surface, $archived)
              ON CONFLICT(workspace_key, thread_id) DO UPDATE SET title = excluded.title, archived = excluded.archived
              """
            : """
              UPDATE usage_threads SET title = $title, archived = $archived
              WHERE workspace_key = $workspace AND thread_id = $thread
              """;
        Add(command, "$workspace", _workspaceKey);
        Add(command, "$thread", thread.Id);
        Add(command, "$title", thread.DisplayName);
        Add(command, "$archived", thread.Status == ThreadStatus.Archived ? 1 : 0);
        if (insert)
        {
            Add(command, "$path", _workspacePath);
            Add(command, "$root", thread.Source.SubAgent?.RootThreadId ?? thread.Id);
            Add(command, "$surface", thread.OriginChannel);
        }
        command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
