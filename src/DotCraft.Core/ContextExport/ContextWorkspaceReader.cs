using System.Text;
using System.Text.Json;
using DotCraft.Protocol;
using Microsoft.Data.Sqlite;

namespace DotCraft.ContextExport;

internal sealed class ContextWorkspaceReader
{
    private static readonly JsonSerializerOptions JsonOptions = SessionJsonOptions.Default;

    public ContextWorkspacePaths ResolvePaths(string? workspacePath)
    {
        var rawPath = string.IsNullOrWhiteSpace(workspacePath)
            ? Directory.GetCurrentDirectory()
            : ExpandHome(workspacePath.Trim());
        var fullPath = Path.GetFullPath(rawPath);

        if (string.Equals(Path.GetFileName(fullPath), ".craft", StringComparison.OrdinalIgnoreCase))
        {
            return new ContextWorkspacePaths(
                WorkspacePath: Path.GetDirectoryName(fullPath) ?? fullPath,
                CraftPath: fullPath);
        }

        return new ContextWorkspacePaths(
            WorkspacePath: fullPath,
            CraftPath: Path.Combine(fullPath, ".craft"));
    }

    public async Task<ContextLoadedThread?> LoadThreadAsync(
        string? workspacePath,
        string threadId,
        CancellationToken ct)
    {
        var paths = ResolvePaths(workspacePath);
        var warnings = new List<string>();
        var rolloutPath = TryResolveRolloutPath(paths, threadId, warnings);
        if (rolloutPath == null || !File.Exists(rolloutPath))
            return null;

        var replay = new ThreadReplay(warnings);
        var lineNumber = 0;
        await foreach (var line in File.ReadLinesAsync(rolloutPath, ct))
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;
            replay.Apply(line, lineNumber);
        }

        var thread = replay.Build();
        if (thread == null)
            return null;
        if (!string.Equals(thread.Id, threadId, StringComparison.Ordinal))
        {
            warnings.Add($"Rollout header thread id does not match requested thread '{threadId}'.");
            return null;
        }

        return new ContextLoadedThread(
            paths,
            thread,
            rolloutPath,
            replay.ContinuityEvents,
            warnings);
    }

    public ContextWorkspaceMemory LoadMemory(
        ContextWorkspacePaths paths,
        ContextExportHistoryMode historyMode,
        int historyTailChars)
    {
        var memoryDir = Path.Combine(paths.CraftPath, "memory");
        var memoryPath = Path.Combine(memoryDir, "MEMORY.md");
        var historyPath = Path.Combine(memoryDir, "HISTORY.md");
        var memory = File.Exists(memoryPath)
            ? File.ReadAllText(memoryPath, Encoding.UTF8)
            : string.Empty;

        var history = string.Empty;
        if (historyMode != ContextExportHistoryMode.None && File.Exists(historyPath))
        {
            var full = File.ReadAllText(historyPath, Encoding.UTF8);
            history = historyMode == ContextExportHistoryMode.Full
                ? full
                : TakeTail(full, Math.Max(0, historyTailChars));
        }

        return new ContextWorkspaceMemory(memoryPath, historyPath, memory, history);
    }

    public IReadOnlyList<ContextThreadIndexRow> LoadThreadIndex(
        string? workspacePath,
        ContextSearchStatusFilter status,
        List<string> warnings)
    {
        var paths = ResolvePaths(workspacePath);
        var dbPath = Path.Combine(paths.CraftPath, "state.db");
        if (!File.Exists(dbPath))
        {
            warnings.Add($"State database not found: {dbPath}");
            return [];
        }

        try
        {
            using var connection = OpenReadOnlyConnection(dbPath);
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT thread_id,
                       rollout_path,
                       workspace_path,
                       origin_channel,
                       channel_context,
                       display_name,
                       status,
                       created_at,
                       updated_at,
                       turn_count,
                       first_user_message,
                       metadata_json
                FROM threads
                ORDER BY updated_at DESC, thread_id DESC
                """;

            var rows = new List<ContextThreadIndexRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var storedStatus = reader.GetString(6);
                if (!MatchesStatusFilter(storedStatus, status))
                    continue;

                var storedRolloutPath = ResolveStoredPath(paths.CraftPath, reader.GetString(1));
                if (storedRolloutPath == null)
                {
                    warnings.Add($"Ignoring rollout path outside craft directory for thread '{reader.GetString(0)}'.");
                    continue;
                }

                rows.Add(new ContextThreadIndexRow(
                    ThreadId: reader.GetString(0),
                    RolloutPath: storedRolloutPath,
                    WorkspacePath: reader.GetString(2),
                    OriginChannel: reader.GetString(3),
                    ChannelContext: reader.IsDBNull(4) ? null : reader.GetString(4),
                    DisplayName: reader.IsDBNull(5) ? null : reader.GetString(5),
                    Status: storedStatus,
                    CreatedAt: ParseDateTimeOffset(reader.GetString(7)),
                    LastActiveAt: ParseDateTimeOffset(reader.GetString(8)),
                    TurnCount: reader.GetInt32(9),
                    FirstUserMessage: reader.IsDBNull(10) ? null : reader.GetString(10),
                    MetadataJson: reader.IsDBNull(11) ? null : reader.GetString(11)));
            }

            return rows;
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            warnings.Add($"Unable to read thread index from {dbPath}: {ex.Message}");
            return [];
        }
    }

    public SqliteConnection? TryOpenStateDb(string? workspacePath, List<string> warnings)
    {
        var paths = ResolvePaths(workspacePath);
        var dbPath = Path.Combine(paths.CraftPath, "state.db");
        if (!File.Exists(dbPath))
        {
            warnings.Add($"State database not found: {dbPath}");
            return null;
        }

        try
        {
            return OpenReadOnlyConnection(dbPath);
        }
        catch (SqliteException ex)
        {
            warnings.Add($"Unable to open state database {dbPath}: {ex.Message}");
            return null;
        }
    }

    public static string TakeTail(string value, int maxChars)
    {
        if (maxChars <= 0 || string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Length <= maxChars)
            return value;

        return value[^maxChars..].TrimStart();
    }

    public static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                    builder.Append(' ');
                previousWhitespace = true;
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    public static string Bound(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0 || value.Length <= maxChars)
            return value;

        return value[..maxChars].TrimEnd() + " ...";
    }

    private string? TryResolveRolloutPath(
        ContextWorkspacePaths paths,
        string threadId,
        List<string> warnings)
    {
        var dbPath = Path.Combine(paths.CraftPath, "state.db");
        if (File.Exists(dbPath))
        {
            try
            {
                using var connection = OpenReadOnlyConnection(dbPath);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT rollout_path FROM threads WHERE thread_id = $thread_id LIMIT 1";
                command.Parameters.AddWithValue("$thread_id", threadId);
                if (command.ExecuteScalar() is string storedPath && !string.IsNullOrWhiteSpace(storedPath))
                {
                    var resolved = ResolveStoredPath(paths.CraftPath, storedPath);
                    if (File.Exists(resolved))
                        return resolved;

                    warnings.Add(resolved == null
                        ? "Thread metadata points to a rollout path outside the craft directory."
                        : $"Thread metadata points to a missing rollout file: {resolved}");
                }
            }
            catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
            {
                warnings.Add($"Unable to read thread metadata from {dbPath}: {ex.Message}");
            }
        }

        return null;
    }

    private static SqliteConnection OpenReadOnlyConnection(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        };
        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA query_only=ON;
            PRAGMA foreign_keys=ON;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static string? ResolveStoredPath(string craftPath, string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        string resolved;
        try
        {
            resolved = Path.GetFullPath(
                Path.IsPathRooted(storedPath)
                    ? storedPath
                    : Path.Combine(craftPath, storedPath));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }

        var root = Path.GetFullPath(craftPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return resolved.StartsWith(root, comparison) ? resolved : null;
    }

    private static bool MatchesStatusFilter(string storedStatus, ContextSearchStatusFilter filter)
    {
        return filter switch
        {
            ContextSearchStatusFilter.All => true,
            ContextSearchStatusFilter.Archived => string.Equals(storedStatus, "Archived", StringComparison.OrdinalIgnoreCase),
            ContextSearchStatusFilter.Active => !string.Equals(storedStatus, "Archived", StringComparison.OrdinalIgnoreCase),
            _ => true
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[2..]);
        }

        return Environment.ExpandEnvironmentVariables(path);
    }

    private sealed class ThreadReplay(List<string> warnings)
    {
        private readonly Dictionary<string, SessionTurn> _turns = new(StringComparer.Ordinal);
        private SessionThread? _thread;
        private bool _hasCanonicalHeader;

        public List<ContextContinuityEvent> ContinuityEvents { get; } = [];

        public void Apply(string line, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            ThreadRolloutRecord? record;
            try
            {
                record = JsonSerializer.Deserialize<ThreadRolloutRecord>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                if (!_hasCanonicalHeader)
                    throw new InvalidDataException("The canonical thread header is unreadable.", ex);
                warnings.Add($"Skipped corrupt rollout line {lineNumber}: {ex.Message}");
                return;
            }

            if (record == null)
            {
                if (!_hasCanonicalHeader)
                    throw new InvalidDataException("The canonical thread header is empty.");
                return;
            }

            if (record.Kind == "thread_opened" && record.ThreadOpened == null)
                throw new InvalidDataException("A canonical thread baseline record is incomplete.");

            if (!_hasCanonicalHeader
                && (record.Kind != "thread_opened" || record.ThreadOpened == null))
            {
                throw new InvalidDataException("The rollout does not begin with a canonical thread header.");
            }

            switch (record.Kind)
            {
                case "thread_opened" when record.ThreadOpened != null:
                    _thread ??= new SessionThread();
                    _thread.Id = record.ThreadOpened.ThreadId;
                    _thread.WorkspacePath = record.ThreadOpened.WorkspacePath;
                    _thread.UserId = record.ThreadOpened.UserId;
                    _thread.OriginChannel = record.ThreadOpened.OriginChannel;
                    _thread.ChannelContext = record.ThreadOpened.ChannelContext;
                    _thread.Source = PersistedThreadSourceCodec.Decode(
                        record.ThreadOpened.Source
                        ?? throw new InvalidDataException("The canonical thread header has no source."));
                    _thread.ForkedFromId = record.ThreadOpened.ForkedFromId;
                    _thread.Ephemeral = record.ThreadOpened.Ephemeral;
                    _thread.Worktree = record.ThreadOpened.Worktree;
                    _thread.CreatedAt = record.ThreadOpened.CreatedAt;
                    _thread.LastActiveAt = record.ThreadOpened.LastActiveAt;
                    _thread.Metadata = new Dictionary<string, string>(record.ThreadOpened.Metadata);
                    _thread.HistoryMode = record.ThreadOpened.HistoryMode;
                    _thread.Configuration = record.ThreadOpened.Configuration;
                    _hasCanonicalHeader = true;
                    break;

                case "thread_name_updated" when _thread != null && record.ThreadNameUpdated != null:
                    _thread.DisplayName = record.ThreadNameUpdated.DisplayName;
                    break;

                case "thread_status_changed" when _thread != null && record.ThreadStatusChanged != null:
                    _thread.Status = record.ThreadStatusChanged.Status;
                    _thread.LastActiveAt = record.ThreadStatusChanged.LastActiveAt;
                    break;

                case "turn_state_replaced" when _thread != null && record.TurnStateReplaced != null:
                    var replacement = record.TurnStateReplaced;
                    var replacementTurn = replacement.Turn;
                    replacementTurn.Input ??= replacementTurn.Items.FirstOrDefault(static item =>
                        item.Type == ItemType.UserMessage);
                    _turns[replacementTurn.Id] = replacementTurn;
                    _thread.Status = replacement.ThreadStatus;
                    _thread.LastActiveAt = replacement.LastActiveAt;
                    _thread.DisplayName = replacement.DisplayName;
                    break;

                case "turn_started" when _thread != null && record.TurnStarted != null:
                    var started = record.TurnStarted.Turn;
                    started.Items = [];
                    started.Input = null;
                    _turns[started.Id] = started;
                    break;

                case "item_appended" when record.ItemAppended != null:
                    if (!_turns.TryGetValue(record.ItemAppended.TurnId, out var turn))
                    {
                        turn = new SessionTurn
                        {
                            Id = record.ItemAppended.TurnId,
                            ThreadId = _thread?.Id ?? string.Empty,
                            Status = TurnStatus.Running,
                            StartedAt = record.Timestamp
                        };
                        _turns[turn.Id] = turn;
                    }

                    var existingIdx = turn.Items.FindIndex(i =>
                        string.Equals(i.Id, record.ItemAppended.Item.Id, StringComparison.Ordinal));
                    if (existingIdx >= 0)
                        turn.Items[existingIdx] = record.ItemAppended.Item;
                    else
                        turn.Items.Add(record.ItemAppended.Item);

                    if (record.ItemAppended.Item.Type == ItemType.UserMessage && turn.Input == null)
                        turn.Input = record.ItemAppended.Item;
                    break;

                case "turn_completed" when record.TurnCompleted != null &&
                                           _turns.TryGetValue(record.TurnCompleted.TurnId, out var completedTurn):
                    completedTurn.Status = record.TurnCompleted.Status;
                    completedTurn.CompletedAt = record.TurnCompleted.CompletedAt;
                    completedTurn.TokenUsage = record.TurnCompleted.TokenUsage;
                    completedTurn.Error = record.TurnCompleted.Error;
                    completedTurn.OriginChannel = record.TurnCompleted.OriginChannel;
                    completedTurn.Initiator = record.TurnCompleted.Initiator;
                    break;

                case "thread_rolled_back" when _thread != null && record.ThreadRolledBack != null:
                    ApplyRollback(_turns, record.ThreadRolledBack.NumTurns);
                    _thread.LastActiveAt = record.ThreadRolledBack.LastActiveAt;
                    ContinuityEvents.Add(ContextContinuityEvent.FromRollback(
                        lineNumber,
                        record.Timestamp,
                        record.ThreadRolledBack.ThreadId,
                        record.ThreadRolledBack.NumTurns));
                    break;

                case "context_compacted" when record.ContextCompacted != null:
                    ContinuityEvents.Add(ContextContinuityEvent.FromCompaction(
                        lineNumber,
                        record.Timestamp,
                        record.ContextCompacted.ThreadId,
                        record.ContextCompacted.CoveredThroughTurnId,
                        record.ContextCompacted.CheckpointId,
                        record.ContextCompacted.Trigger,
                        record.ContextCompacted.Mode,
                        record.ContextCompacted.TokensBefore,
                        record.ContextCompacted.TokensAfter,
                        record.ContextCompacted.CreatedAt));
                    break;

                case "queued_input_added" when _thread != null && record.QueuedInputAdded != null:
                    if (_thread.QueuedInputs.All(q =>
                            !string.Equals(q.Id, record.QueuedInputAdded.QueuedInput.Id, StringComparison.Ordinal)))
                    {
                        _thread.QueuedInputs.Add(record.QueuedInputAdded.QueuedInput);
                    }
                    break;

                case "queued_input_removed" when _thread != null && record.QueuedInputRemoved != null:
                    _thread.QueuedInputs.RemoveAll(q =>
                        string.Equals(q.Id, record.QueuedInputRemoved.QueuedInputId, StringComparison.Ordinal));
                    _thread.LastActiveAt = record.QueuedInputRemoved.LastActiveAt;
                    break;

                case "queued_input_updated" when _thread != null && record.QueuedInputUpdated != null:
                    var updateIndex = _thread.QueuedInputs.FindIndex(q =>
                        string.Equals(q.Id, record.QueuedInputUpdated.QueuedInput.Id, StringComparison.Ordinal));
                    if (updateIndex >= 0)
                        _thread.QueuedInputs[updateIndex] = record.QueuedInputUpdated.QueuedInput;
                    _thread.LastActiveAt = record.QueuedInputUpdated.LastActiveAt;
                    break;

                case "queued_input_reordered" when _thread != null && record.QueuedInputReordered != null:
                    var queuedById = _thread.QueuedInputs.ToDictionary(q => q.Id, StringComparer.Ordinal);
                    var seenQueuedIds = new HashSet<string>(StringComparer.Ordinal);
                    var reorderedQueue = new List<QueuedTurnInput>(_thread.QueuedInputs.Count);
                    foreach (var queuedInputId in record.QueuedInputReordered.OrderedQueuedInputIds)
                    {
                        if (seenQueuedIds.Add(queuedInputId) && queuedById.TryGetValue(queuedInputId, out var queuedInput))
                            reorderedQueue.Add(queuedInput);
                    }

                    reorderedQueue.AddRange(_thread.QueuedInputs.Where(q => !seenQueuedIds.Contains(q.Id)));
                    _thread.QueuedInputs = reorderedQueue;
                    _thread.LastActiveAt = record.QueuedInputReordered.LastActiveAt;
                    break;
            }
        }

        public SessionThread? Build()
        {
            if (_thread == null)
                return null;

            _thread.Turns = _turns.Values.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
            return _thread;
        }

        private static void ApplyRollback(Dictionary<string, SessionTurn> turns, int numTurns)
        {
            if (numTurns <= 0 || turns.Count == 0)
                return;

            var idsToRemove = turns.Values
                .OrderBy(t => t.StartedAt)
                .ThenBy(t => t.Id, StringComparer.Ordinal)
                .TakeLast(numTurns)
                .Select(t => t.Id)
                .ToList();

            foreach (var id in idsToRemove)
                turns.Remove(id);
        }
    }
}

internal sealed record ContextWorkspacePaths(string WorkspacePath, string CraftPath);

internal sealed record ContextWorkspaceMemory(
    string MemoryPath,
    string HistoryPath,
    string Memory,
    string History);

internal sealed record ContextLoadedThread(
    ContextWorkspacePaths Paths,
    SessionThread Thread,
    string RolloutPath,
    IReadOnlyList<ContextContinuityEvent> ContinuityEvents,
    IReadOnlyList<string> Warnings);

internal enum ContextContinuityEventKind
{
    Rollback,
    Compaction
}

internal sealed record ContextContinuityEvent(
    ContextContinuityEventKind Kind,
    int LineNumber,
    DateTimeOffset Timestamp,
    string ThreadId,
    int? NumTurns,
    string? CoveredThroughTurnId,
    string? CheckpointId,
    string? Trigger,
    string? Mode,
    long? TokensBefore,
    long? TokensAfter,
    DateTimeOffset? CreatedAt)
{
    public static ContextContinuityEvent FromRollback(
        int lineNumber,
        DateTimeOffset timestamp,
        string threadId,
        int numTurns) =>
        new(
            ContextContinuityEventKind.Rollback,
            lineNumber,
            timestamp,
            threadId,
            numTurns,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    public static ContextContinuityEvent FromCompaction(
        int lineNumber,
        DateTimeOffset timestamp,
        string threadId,
        string coveredThroughTurnId,
        string checkpointId,
        string trigger,
        string mode,
        long tokensBefore,
        long tokensAfter,
        DateTimeOffset createdAt) =>
        new(
            ContextContinuityEventKind.Compaction,
            lineNumber,
            timestamp,
            threadId,
            null,
            coveredThroughTurnId,
            checkpointId,
            trigger,
            mode,
            tokensBefore,
            tokensAfter,
            createdAt);
}

internal sealed record ContextThreadIndexRow(
    string ThreadId,
    string RolloutPath,
    string WorkspacePath,
    string OriginChannel,
    string? ChannelContext,
    string? DisplayName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActiveAt,
    int TurnCount,
    string? FirstUserMessage,
    string? MetadataJson);
