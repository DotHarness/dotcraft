using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using DotCraft.Sessions;
using SessionThread = DotCraft.Sessions.SessionThread;

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

        var continuityEvents = new List<ContextContinuityEvent>();
        var lineNumber = 0;
        await foreach (var line in File.ReadLinesAsync(rolloutPath, ct))
        {
            ct.ThrowIfCancellationRequested();
            lineNumber++;
            try
            {
                var record = JsonSerializer.Deserialize<ContextRolloutRecord>(line, JsonOptions);
                if (record is { Kind: "thread_rolled_back", ThreadRolledBack: { } rollback })
                {
                    continuityEvents.Add(ContextContinuityEvent.FromRollback(
                        lineNumber,
                        record.Timestamp,
                        rollback.ThreadId,
                        rollback.NumTurns));
                }
                else if (record is { Kind: "context_compacted", ContextCompacted: { } compaction })
                {
                    continuityEvents.Add(ContextContinuityEvent.FromCompaction(
                        lineNumber,
                        record.Timestamp,
                        compaction.ThreadId,
                        compaction.CoveredThroughTurnId,
                        compaction.CheckpointId,
                        compaction.Trigger,
                        compaction.Mode,
                        compaction.TokensBefore,
                        compaction.TokensAfter,
                        compaction.CreatedAt));
                }
            }
            catch (JsonException ex)
            {
                warnings.Add($"Skipped corrupt rollout line {lineNumber}: {ex.Message}");
            }
        }

        var thread = await new SessionRolloutReader().ReadAsync(rolloutPath, ct).ConfigureAwait(false);
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
            continuityEvents,
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
