using Microsoft.Data.Sqlite;

namespace DotCraft.State;

public sealed class StateRuntime
{
    private const double DefaultCompactFreelistRatio = 0.25;
    private const int DefaultCompactMinFreelistPages = 32;

    private readonly string _connectionString;
    private readonly bool _readOnly;
    private readonly object _initLock = new();
    private bool _initialized;

    public StateRuntime(string botPath)
        : this(botPath, readOnly: false)
    {
    }

    /// <summary>
    /// Creates a workspace state runtime.
    /// </summary>
    /// <param name="botPath">Path to the workspace <c>.craft</c> directory.</param>
    /// <param name="readOnly">
    /// When true, opens the existing state database without creating directories,
    /// creating schema, running migrations, or issuing write-oriented pragmas.
    /// </param>
    public StateRuntime(string botPath, bool readOnly)
    {
        _readOnly = readOnly;
        if (!readOnly)
            Directory.CreateDirectory(botPath);

        DbPath = Path.Combine(botPath, "state.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        if (!readOnly)
            EnsureInitialized();
    }

    public string DbPath { get; }

    public SqliteConnection OpenConnection()
    {
        if (!_readOnly)
            EnsureInitialized();

        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = _readOnly
            ? """
              PRAGMA query_only=ON;
              PRAGMA foreign_keys=ON;
              """
            : """
              PRAGMA journal_mode=WAL;
              PRAGMA synchronous=NORMAL;
              PRAGMA foreign_keys=ON;
              PRAGMA secure_delete=ON;
              """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>
    /// Truncates the SQLite write-ahead log for this workspace state database.
    /// </summary>
    public void CheckpointWalTruncate()
    {
        using var connection = OpenConnection();
        CheckpointWalTruncate(connection);
    }

    /// <summary>
    /// Reclaims free SQLite pages when the database has enough reusable space to justify compaction.
    /// </summary>
    /// <returns><c>true</c> when VACUUM was executed; otherwise <c>false</c>.</returns>
    public bool CompactIfWorthwhile(
        bool force = false,
        double minFreelistRatio = DefaultCompactFreelistRatio,
        int minFreelistPages = DefaultCompactMinFreelistPages)
    {
        using var connection = OpenConnection();
        var pageCount = ReadPragmaLong(connection, "page_count");
        var freelistCount = ReadPragmaLong(connection, "freelist_count");
        var ratio = pageCount <= 0 ? 0 : (double)freelistCount / pageCount;
        var shouldCompact = force
            || (freelistCount >= minFreelistPages && ratio >= minFreelistRatio);

        if (!shouldCompact)
        {
            CheckpointWalTruncate(connection);
            return false;
        }

        using (var vacuum = connection.CreateCommand())
        {
            vacuum.CommandText = "VACUUM";
            vacuum.ExecuteNonQuery();
        }

        CheckpointWalTruncate(connection);
        return true;
    }

    public string? GetInfo(string key)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM state_info WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetInfo(string key, string value)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO state_info(key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private void EnsureInitialized()
    {
        if (_readOnly)
            return;

        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode=WAL;
                PRAGMA synchronous=NORMAL;
                PRAGMA foreign_keys=ON;
                PRAGMA secure_delete=ON;

                CREATE TABLE IF NOT EXISTS state_info (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS threads (
                    thread_id TEXT PRIMARY KEY,
                    rollout_path TEXT NOT NULL,
                    workspace_path TEXT NOT NULL,
                    user_id TEXT,
                    origin_channel TEXT NOT NULL,
                    channel_context TEXT,
                    forked_from_id TEXT,
                    ephemeral INTEGER NOT NULL DEFAULT 0,
                    worktree_json TEXT,
                    display_name TEXT,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    archived_at TEXT,
                    history_mode TEXT NOT NULL,
                    turn_count INTEGER NOT NULL DEFAULT 0,
                    first_user_message TEXT,
                    metadata_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_threads_updated_at ON threads(updated_at DESC, thread_id DESC);
                CREATE INDEX IF NOT EXISTS idx_threads_workspace_identity
                    ON threads(workspace_path, user_id, channel_context, origin_channel);
                CREATE INDEX IF NOT EXISTS idx_threads_status ON threads(status);

                CREATE TABLE IF NOT EXISTS thread_sessions (
                    thread_id TEXT PRIMARY KEY,
                    session_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS thread_context_usage (
                    thread_id TEXT PRIMARY KEY,
                    context_usage_tokens INTEGER NOT NULL,
                    message_count INTEGER,
                    prefix_fingerprint TEXT,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS thread_goals (
                    thread_id TEXT PRIMARY KEY,
                    goal_id TEXT NOT NULL,
                    objective TEXT NOT NULL,
                    status TEXT NOT NULL CHECK(status IN ('active', 'paused', 'budget_limited', 'complete')),
                    token_budget INTEGER,
                    input_tokens INTEGER NOT NULL DEFAULT 0,
                    output_tokens INTEGER NOT NULL DEFAULT 0,
                    cached_input_tokens INTEGER NOT NULL DEFAULT 0,
                    cache_write_input_tokens INTEGER NOT NULL DEFAULT 0,
                    reasoning_output_tokens INTEGER NOT NULL DEFAULT 0,
                    total_tokens INTEGER NOT NULL DEFAULT 0,
                    time_used_seconds INTEGER NOT NULL DEFAULT 0,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS thread_plans (
                    thread_id TEXT PRIMARY KEY,
                    plan_json TEXT,
                    rendered_markdown TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS item_widget_state (
                    thread_id TEXT NOT NULL,
                    call_id TEXT NOT NULL,
                    widget_state_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY(thread_id, call_id),
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS thread_attachments (
                    ref_id TEXT PRIMARY KEY,
                    path TEXT NOT NULL,
                    thread_id TEXT NOT NULL,
                    turn_id TEXT,
                    item_id TEXT,
                    kind TEXT NOT NULL,
                    bytes INTEGER,
                    created_at TEXT NOT NULL,
                    last_seen_at TEXT NOT NULL,
                    FOREIGN KEY(thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_thread_attachments_thread
                    ON thread_attachments(thread_id);
                CREATE INDEX IF NOT EXISTS idx_thread_attachments_path
                    ON thread_attachments(path);

                CREATE TABLE IF NOT EXISTS thread_spawn_edges (
                    parent_thread_id TEXT NOT NULL,
                    child_thread_id TEXT NOT NULL,
                    parent_turn_id TEXT,
                    depth INTEGER NOT NULL DEFAULT 1,
                    agent_path TEXT,
                    task_name TEXT,
                    agent_nickname TEXT,
                    agent_role TEXT,
                    profile_name TEXT,
                    runtime_type TEXT,
                    supports_send_input INTEGER NOT NULL DEFAULT 0,
                    supports_resume INTEGER NOT NULL DEFAULT 0,
                    supports_send_message INTEGER NOT NULL DEFAULT 0,
                    supports_followup_task INTEGER NOT NULL DEFAULT 0,
                    supports_close INTEGER NOT NULL DEFAULT 1,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    PRIMARY KEY(parent_thread_id, child_thread_id),
                    FOREIGN KEY(parent_thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE,
                    FOREIGN KEY(child_thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_thread_spawn_edges_parent
                    ON thread_spawn_edges(parent_thread_id, status, updated_at DESC);
                CREATE INDEX IF NOT EXISTS idx_thread_spawn_edges_child
                    ON thread_spawn_edges(child_thread_id);

                CREATE TABLE IF NOT EXISTS subagent_mailbox_entries (
                    id TEXT PRIMARY KEY,
                    root_thread_id TEXT NOT NULL,
                    sender_agent_path TEXT NOT NULL,
                    target_agent_path TEXT NOT NULL,
                    message TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    delivered_at TEXT,
                    FOREIGN KEY(root_thread_id) REFERENCES threads(thread_id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_subagent_mailbox_target
                    ON subagent_mailbox_entries(root_thread_id, target_agent_path, status, created_at);
                CREATE INDEX IF NOT EXISTS idx_subagent_mailbox_status
                    ON subagent_mailbox_entries(root_thread_id, status, created_at);

                CREATE TABLE IF NOT EXISTS trace_sessions (
                    session_key TEXT PRIMARY KEY,
                    started_at TEXT NOT NULL,
                    last_activity_at TEXT NOT NULL,
                    request_count INTEGER NOT NULL DEFAULT 0,
                    maintenance_fork_request_count INTEGER NOT NULL DEFAULT 0,
                    response_count INTEGER NOT NULL DEFAULT 0,
                    maintenance_fork_response_count INTEGER NOT NULL DEFAULT 0,
                    tool_call_count INTEGER NOT NULL DEFAULT 0,
                    error_count INTEGER NOT NULL DEFAULT 0,
                    context_compaction_count INTEGER NOT NULL DEFAULT 0,
                    thinking_count INTEGER NOT NULL DEFAULT 0,
                    token_usage_count INTEGER NOT NULL DEFAULT 0,
                    total_input_tokens INTEGER NOT NULL DEFAULT 0,
                    total_output_tokens INTEGER NOT NULL DEFAULT 0,
                    total_cached_input_tokens INTEGER NOT NULL DEFAULT 0,
                    total_cache_write_input_tokens INTEGER NOT NULL DEFAULT 0,
                    total_reasoning_output_tokens INTEGER NOT NULL DEFAULT 0,
                    total_tool_duration_ms INTEGER NOT NULL DEFAULT 0,
                    max_tool_duration_ms INTEGER NOT NULL DEFAULT 0,
                    max_turn_duration_ms INTEGER NOT NULL DEFAULT 0,
                    last_finish_reason TEXT,
                    final_system_prompt TEXT,
                    tool_names_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_trace_sessions_last_activity
                    ON trace_sessions(last_activity_at DESC, session_key DESC);

                CREATE TABLE IF NOT EXISTS trace_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_id TEXT NOT NULL,
                    session_key TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    type TEXT NOT NULL,
                    tool_name TEXT,
                    call_id TEXT,
                    response_id TEXT,
                    message_id TEXT,
                    model_id TEXT,
                    finish_reason TEXT,
                    duration_ms REAL,
                    event_json TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_trace_events_session_ts
                    ON trace_events(session_key, timestamp, id);
                CREATE INDEX IF NOT EXISTS idx_trace_events_ts
                    ON trace_events(timestamp, id);

                CREATE TABLE IF NOT EXISTS trace_session_bindings (
                    session_key TEXT PRIMARY KEY,
                    root_thread_id TEXT,
                    parent_session_key TEXT,
                    binding_kind TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_trace_bindings_root_thread
                    ON trace_session_bindings(root_thread_id, session_key);
                CREATE INDEX IF NOT EXISTS idx_trace_bindings_parent_session
                    ON trace_session_bindings(parent_session_key, session_key);
                CREATE INDEX IF NOT EXISTS idx_trace_bindings_kind
                    ON trace_session_bindings(binding_kind, session_key);

                CREATE TABLE IF NOT EXISTS token_usage_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    user_id TEXT NOT NULL,
                    display_name TEXT NOT NULL,
                    group_id INTEGER,
                    group_name TEXT,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    cached_input_tokens INTEGER NOT NULL DEFAULT 0,
                    cache_write_input_tokens INTEGER NOT NULL DEFAULT 0,
                    reasoning_output_tokens INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_token_usage_channel_ts
                    ON token_usage_records(channel, timestamp DESC, id DESC);

                CREATE TABLE IF NOT EXISTS dashboard_usage_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    source_mode TEXT NOT NULL,
                    subject_kind TEXT NOT NULL,
                    subject_id TEXT NOT NULL,
                    subject_label TEXT NOT NULL,
                    context_kind TEXT,
                    context_id TEXT,
                    context_label TEXT,
                    thread_id TEXT,
                    session_key TEXT,
                    llm_call_count INTEGER NOT NULL DEFAULT 1,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    cached_input_tokens INTEGER NOT NULL DEFAULT 0,
                    cache_write_input_tokens INTEGER NOT NULL DEFAULT 0,
                    reasoning_output_tokens INTEGER NOT NULL DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_ts
                    ON dashboard_usage_records(source_id, timestamp DESC, id DESC);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_subject
                    ON dashboard_usage_records(source_id, subject_kind, subject_id);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_source_context
                    ON dashboard_usage_records(source_id, context_kind, context_id);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_thread
                    ON dashboard_usage_records(thread_id, timestamp DESC, id DESC);
                CREATE INDEX IF NOT EXISTS idx_dashboard_usage_session
                    ON dashboard_usage_records(session_key, timestamp DESC, id DESC);
                """;
            command.ExecuteNonQuery();
            EnsureColumn(connection, "thread_spawn_edges", "runtime_type", "TEXT");
            EnsureColumn(connection, "thread_spawn_edges", "agent_path", "TEXT");
            EnsureColumn(connection, "thread_spawn_edges", "task_name", "TEXT");
            EnsureColumn(connection, "threads", "metadata_json", "TEXT");
            EnsureColumn(connection, "threads", "forked_from_id", "TEXT");
            EnsureColumn(connection, "threads", "ephemeral", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "threads", "worktree_json", "TEXT");
            EnsureColumn(connection, "thread_spawn_edges", "supports_send_input", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "thread_spawn_edges", "supports_resume", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "thread_spawn_edges", "supports_send_message", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "thread_spawn_edges", "supports_followup_task", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "thread_spawn_edges", "supports_close", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(connection, "trace_sessions", "total_cached_input_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "token_usage_count", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "total_cache_write_input_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "total_reasoning_output_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "maintenance_fork_request_count", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "maintenance_fork_response_count", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_sessions", "max_turn_duration_ms", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "trace_events", "reasoning_effort", "TEXT");
            EnsureColumn(connection, "token_usage_records", "cache_write_input_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "dashboard_usage_records", "cached_input_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "dashboard_usage_records", "cache_write_input_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "dashboard_usage_records", "llm_call_count", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(connection, "dashboard_usage_records", "reasoning_output_tokens", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(connection, "thread_context_usage", "message_count", "INTEGER");
            EnsureColumn(connection, "thread_context_usage", "prefix_fingerprint", "TEXT");

            _initialized = true;
        }
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA table_info({tableName})";
            using var reader = pragma.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        alter.ExecuteNonQuery();
    }

    private static long ReadPragmaLong(SqliteConnection connection, string pragmaName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragmaName}";
        var value = command.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    private static void CheckpointWalTruncate(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Drain the pragma result set.
        }
    }
}
