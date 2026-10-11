using DotCraft.Persistence;

namespace DotCraft.Visualizations;

/// <summary>A visualization's saved snapshot; each field is serialized JSON or <c>null</c>.</summary>
public sealed record VisualizationWidgetState(string? ModelContentJson, string? PrivateContentJson);

/// <summary>A visualization whose saved snapshot carries model content.</summary>
public sealed record VisualizationModelContent(string PathKey, string Path, string ModelContentJson);

/// <summary>Stores one widget state snapshot per thread and visualization path.</summary>
public sealed class VisualizationWidgetStateStore(WorkspaceStateDatabase database)
{
    public VisualizationWidgetState? Load(string threadId, string path)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_content_json, private_content_json
            FROM visualization_widget_state
            WHERE thread_id = $thread_id AND path_key = $path_key
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$path_key", PathKey(path));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new VisualizationWidgetState(
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    /// <summary>Replaces the snapshot; a snapshot whose fields are both <c>null</c> removes it.</summary>
    public void Save(string threadId, string path, VisualizationWidgetState state)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$path_key", PathKey(path));
        if (state.ModelContentJson is null && state.PrivateContentJson is null)
        {
            command.CommandText = "DELETE FROM visualization_widget_state WHERE thread_id = $thread_id AND path_key = $path_key";
            command.ExecuteNonQuery();
            return;
        }

        command.CommandText = """
            INSERT INTO visualization_widget_state(thread_id, path_key, path, model_content_json, private_content_json, updated_at)
            VALUES ($thread_id, $path_key, $path, $model_content, $private_content, $updated_at)
            ON CONFLICT(thread_id, path_key) DO UPDATE SET
                path = excluded.path,
                model_content_json = excluded.model_content_json,
                private_content_json = excluded.private_content_json,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$path", Path.GetFullPath(path));
        command.Parameters.AddWithValue("$model_content", (object?)state.ModelContentJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$private_content", (object?)state.PrivateContentJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<VisualizationModelContent> LoadModelContents(string threadId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT path_key, path, model_content_json
            FROM visualization_widget_state
            WHERE thread_id = $thread_id AND model_content_json IS NOT NULL
            ORDER BY path_key
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        var result = new List<VisualizationModelContent>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new VisualizationModelContent(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static string PathKey(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }
}
