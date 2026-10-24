using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence.Upgrades;

internal sealed class WorkspaceUpgrade_0_9_3 : IWorkspaceUpgrade
{
    private const int NoTurn = -1;
    private const int ThreadOpenedLine = -2;

    public string Version => "0.9.3";

    public void Apply(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        if (HasColumn(connection, transaction, "threads", "workspace_path"))
        {
            Execute(connection, transaction, "DROP INDEX IF EXISTS idx_threads_workspace_identity");
            Execute(connection, transaction, "ALTER TABLE threads DROP COLUMN workspace_path");
        }

        RelativizeRolloutPaths(connection, transaction, "threads");
        RelativizeRolloutPaths(connection, transaction, "thread_history_projection_state");
        transaction.Commit();
    }

    public async Task ApplyInBackgroundAsync(ThreadStore threadStore, CancellationToken ct)
    {
        foreach (var status in new[] { "active", "archived" })
        {
            var directory = Path.Combine(threadStore.DataPath, "threads", status);
            if (!Directory.Exists(directory))
                continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!HasWorkspacePathHeader(path))
                        continue;
                    await threadStore.WithExclusiveRolloutAsync(
                        Path.GetFileNameWithoutExtension(path),
                        rolloutPath => CompactLegacyRollout(threadStore, rolloutPath, ct),
                        ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        $"Unable to compact rollout '{Path.GetFileName(path)}': {ex.Message}");
                }
            }
        }
    }

    private static void CompactLegacyRollout(ThreadStore threadStore, string path, CancellationToken ct)
    {
        if (!HasWorkspacePathHeader(path))
            return;

        var threadId = Path.GetFileNameWithoutExtension(path);
        using (var connection = threadStore.StateDatabase.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            Execute(connection, transaction, "DELETE FROM thread_history_projection_state WHERE thread_id = $thread_id", threadId);
            Execute(connection, transaction, "UPDATE threads SET projected_rollout_offset = 0 WHERE thread_id = $thread_id", threadId);
            transaction.Commit();
        }

        CompactRollout(path, ct);
    }

    internal static bool HasWorkspacePathHeader(string path)
    {
        var firstLine = ReadLines(path).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return false;

        try
        {
            using var document = JsonDocument.Parse(firstLine);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                && kind.GetString() == RolloutKinds.ThreadOpened
                && root.TryGetProperty("threadOpened", out var header)
                && header.ValueKind == JsonValueKind.Object
                && header.TryGetProperty("workspacePath", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static void CompactRollout(string path, CancellationToken ct = default)
    {
        var turnIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstRecord = new List<long>();
        var lastReplacement = new List<long>();
        var rollbacks = new List<long>();
        var lineTurns = new List<int>();

        long index = 0;
        foreach (var line in ReadLines(path))
        {
            ct.ThrowIfCancellationRequested();
            var (kind, turnId) = Describe(line);
            if (kind == RolloutKinds.ThreadRolledBack)
                rollbacks.Add(index);

            var turn = kind == RolloutKinds.ThreadOpened ? ThreadOpenedLine : NoTurn;
            if (turnId != null)
            {
                if (!turnIds.TryGetValue(turnId, out turn))
                {
                    turn = turnIds.Count;
                    turnIds.Add(turnId, turn);
                    firstRecord.Add(index);
                    lastReplacement.Add(-1);
                }
                if (kind == RolloutKinds.TurnStateReplaced)
                    lastReplacement[turn] = index;
            }

            lineTurns.Add(turn);
            index++;
        }

        var supersededBefore = new long[turnIds.Count];
        for (var turn = 0; turn < supersededBefore.Length; turn++)
        {
            var replacedAt = lastReplacement[turn];
            var first = firstRecord[turn];
            supersededBefore[turn] = replacedAt >= 0 && !rollbacks.Any(rollback => rollback > first && rollback < replacedAt)
                ? replacedAt
                : -1;
        }

        var temporaryPath = path + ".upgrade";
        try
        {
            using (var writer = new StreamWriter(temporaryPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                index = 0;
                foreach (var line in ReadLines(path))
                {
                    ct.ThrowIfCancellationRequested();
                    var turn = lineTurns[(int)index];
                    var current = index++;
                    if (turn >= 0 && current < supersededBefore[turn])
                        continue;

                    if (turn == ThreadOpenedLine
                        && JsonNode.Parse(line) is JsonObject record
                        && record["threadOpened"] is JsonObject header
                        && header.Remove("workspacePath"))
                    {
                        writer.Write(record.ToJsonString());
                    }
                    else
                    {
                        writer.Write(line);
                    }
                    writer.Write('\n');
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    private static (string? Kind, string? TurnId) Describe(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return (null, null);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return (null, null);
        }

        using var _ = document;
        var root = document.RootElement;
        var kind = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("kind", out var kindElement)
            ? kindElement.GetString()
            : null;
        return kind switch
        {
            RolloutKinds.TurnStarted => (kind, Read(root, "turnStarted", "turn", "id")),
            RolloutKinds.ItemAppended => (kind, Read(root, "itemAppended", "turnId")),
            RolloutKinds.TurnCompleted => (kind, Read(root, "turnCompleted", "turnId")),
            RolloutKinds.TurnStateReplaced => (kind, Read(root, "turnStateReplaced", "turn", "id")),
            _ => (kind, null)
        };
    }

    private static string? Read(JsonElement element, params string[] path)
    {
        foreach (var name in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    private static void RelativizeRolloutPaths(SqliteConnection connection, SqliteTransaction transaction, string table)
    {
        var rows = new List<(string ThreadId, string Path)>();
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = $"SELECT thread_id, rollout_path FROM {table}";
            using var reader = select.ExecuteReader();
            while (reader.Read())
                rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (threadId, rolloutPath) in rows.Where(static row => Path.IsPathRooted(row.Path)))
        {
            var relative = Path.Combine(
                "threads",
                Path.GetFileName(Path.GetDirectoryName(rolloutPath))!,
                Path.GetFileName(rolloutPath));
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = $"UPDATE {table} SET rollout_path = $path WHERE thread_id = $thread_id";
            update.Parameters.AddWithValue("$path", relative);
            update.Parameters.AddWithValue("$thread_id", threadId);
            update.ExecuteNonQuery();
        }
    }

    private static bool HasColumn(SqliteConnection connection, SqliteTransaction transaction, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql, string? threadId = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        if (threadId != null)
            command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }
}
