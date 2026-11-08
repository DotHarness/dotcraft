using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence;

public sealed class UsageStatisticsDatabase : IDisposable
{
    private readonly string _connectionString;
    private readonly bool _readOnly;

    public UsageStatisticsDatabase(string userDataPath, bool readOnly = false)
    {
        _readOnly = readOnly;
        var databasePath = Path.GetFullPath(Path.Combine(userDataPath, "usage.db"));
        if (!readOnly)
            Directory.CreateDirectory(userDataPath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        }.ToString();
        if (!readOnly)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS usage_turns (
                    workspace_key TEXT NOT NULL,
                    thread_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    root_thread_id TEXT NOT NULL,
                    surface TEXT NOT NULL,
                    source_id TEXT NOT NULL,
                    source_mode TEXT NOT NULL,
                    subject_kind TEXT NOT NULL,
                    subject_id TEXT NOT NULL,
                    subject_label TEXT,
                    context_kind TEXT,
                    context_id TEXT,
                    context_label TEXT,
                    model TEXT,
                    reasoning_effort TEXT,
                    speed TEXT,
                    llm_call_count INTEGER NOT NULL,
                    input_tokens INTEGER NOT NULL,
                    output_tokens INTEGER NOT NULL,
                    cached_input_tokens INTEGER NOT NULL,
                    cache_write_input_tokens INTEGER NOT NULL,
                    reasoning_output_tokens INTEGER NOT NULL,
                    cost_usd REAL,
                    PRIMARY KEY (workspace_key, thread_id, turn_id)
                );
                CREATE INDEX IF NOT EXISTS idx_usage_turns_timestamp ON usage_turns(timestamp);
                CREATE INDEX IF NOT EXISTS idx_usage_turns_root ON usage_turns(workspace_key, root_thread_id);
                CREATE TABLE IF NOT EXISTS usage_events (
                    workspace_key TEXT NOT NULL,
                    event_id TEXT NOT NULL,
                    timestamp TEXT NOT NULL,
                    type TEXT NOT NULL,
                    surface TEXT NOT NULL,
                    model_id TEXT,
                    reasoning_effort TEXT,
                    tool_name TEXT,
                    tool_source TEXT,
                    duration_ms REAL,
                    PRIMARY KEY (workspace_key, event_id)
                );
                CREATE INDEX IF NOT EXISTS idx_usage_events_type_timestamp ON usage_events(type, timestamp);
                CREATE TABLE IF NOT EXISTS usage_threads (
                    workspace_key TEXT NOT NULL,
                    workspace_path TEXT NOT NULL,
                    thread_id TEXT NOT NULL,
                    root_thread_id TEXT NOT NULL,
                    title TEXT,
                    origin_channel TEXT NOT NULL,
                    archived INTEGER NOT NULL,
                    PRIMARY KEY (workspace_key, thread_id)
                );
                """;
            command.ExecuteNonQuery();
        }
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = _readOnly
            ? "PRAGMA query_only=ON;"
            : "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA secure_delete=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    internal static string WorkspaceKey(string workspacePath)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
    }

    public void Dispose()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}
