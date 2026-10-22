using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Data.Sqlite;

namespace DotCraft.Persistence.Upgrades;

internal sealed class WorkspaceUpgrade_0_9_2 : IWorkspaceUpgrade
{
    public string Version => "0.9.2";

    public void Apply(SqliteConnection connection, string dataPath)
    {
        var compacted = new List<string>();
        foreach (var status in new[] { "active", "archived" })
        {
            var directory = Path.Combine(dataPath, "threads", status);
            if (!Directory.Exists(directory))
                continue;
            foreach (var path in Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly))
            {
                if (CompactRollout(path))
                    compacted.Add(Path.GetFileNameWithoutExtension(path));
            }
        }

        using var transaction = connection.BeginTransaction();
        if (HasColumn(connection, transaction, "threads", "workspace_path"))
        {
            Execute(connection, transaction, "DROP INDEX IF EXISTS idx_threads_workspace_identity");
            Execute(connection, transaction, "ALTER TABLE threads DROP COLUMN workspace_path");
        }

        RelativizeRolloutPaths(connection, transaction, "threads");
        RelativizeRolloutPaths(connection, transaction, "thread_history_projection_state");

        foreach (var threadId in compacted)
        {
            Execute(connection, transaction, "DELETE FROM thread_history_projection_state WHERE thread_id = $thread_id", threadId);
            Execute(connection, transaction, "UPDATE threads SET projected_rollout_offset = 0 WHERE thread_id = $thread_id", threadId);
        }

        transaction.Commit();
    }

    /// <summary>
    /// A <c>turn_state_replaced</c> record carries the whole Turn, so the Turn's earlier
    /// started, item, and completed records add nothing to replay.
    /// </summary>
    internal static bool CompactRollout(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        var records = lines.Select(Describe).ToArray();

        var lastReplacement = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstRecord = new Dictionary<string, int>(StringComparer.Ordinal);
        var rollbacks = new List<int>();
        for (var index = 0; index < records.Length; index++)
        {
            var (kind, turnId, _) = records[index];
            if (kind == RolloutKinds.ThreadRolledBack)
                rollbacks.Add(index);
            if (turnId == null)
                continue;
            firstRecord.TryAdd(turnId, index);
            if (kind == RolloutKinds.TurnStateReplaced)
                lastReplacement[turnId] = index;
        }

        var changed = false;
        var output = new StringBuilder();
        for (var index = 0; index < records.Length; index++)
        {
            var (_, turnId, opened) = records[index];
            if (turnId != null
                && lastReplacement.TryGetValue(turnId, out var replacedAt)
                && index < replacedAt
                && !rollbacks.Any(rollback => rollback > firstRecord[turnId] && rollback < replacedAt))
            {
                changed = true;
                continue;
            }

            if (opened?["threadOpened"] is JsonObject header && header.Remove("workspacePath"))
            {
                changed = true;
                output.Append(opened.ToJsonString()).Append('\n');
                continue;
            }

            output.Append(lines[index]).Append('\n');
        }

        if (!changed)
            return false;

        var temporaryPath = path + ".upgrade";
        File.WriteAllText(temporaryPath, output.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, path, overwrite: true);
        return true;
    }

    private static (string? Kind, string? TurnId, JsonObject? Opened) Describe(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return (null, null, null);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return (null, null, null);
        }

        using var _ = document;
        var root = document.RootElement;
        var kind = root.TryGetProperty("kind", out var kindElement) ? kindElement.GetString() : null;
        return kind switch
        {
            RolloutKinds.ThreadOpened => (kind, null, JsonNode.Parse(line) as JsonObject),
            RolloutKinds.TurnStarted => (kind, Read(root, "turnStarted", "turn", "id"), null),
            RolloutKinds.ItemAppended => (kind, Read(root, "itemAppended", "turnId"), null),
            RolloutKinds.TurnCompleted => (kind, Read(root, "turnCompleted", "turnId"), null),
            RolloutKinds.TurnStateReplaced => (kind, Read(root, "turnStateReplaced", "turn", "id"), null),
            _ => (kind, null, null)
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
