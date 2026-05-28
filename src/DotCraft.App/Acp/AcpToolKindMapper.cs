namespace DotCraft.Acp;

/// <summary>
/// Maps DotCraft tool names to ACP tool call kinds.
/// </summary>
public static class AcpToolKindMapper
{
    private static readonly Dictionary<string, string> ToolKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReadFile"] = AcpToolKind.Read,
        ["WriteFile"] = AcpToolKind.Edit,
        ["EditFile"] = AcpToolKind.Edit,
        ["GrepFiles"] = AcpToolKind.Search,
        ["FindFiles"] = AcpToolKind.Search,
        ["Exec"] = AcpToolKind.Execute,
        ["WebSearch"] = AcpToolKind.Fetch,
        ["WebFetch"] = AcpToolKind.Fetch,
        ["SpawnAgent"] = AcpToolKind.Think,
        ["CreatePlan"] = AcpToolKind.Other,
        ["UpdateTodos"] = AcpToolKind.Other
    };

    /// <summary>
    /// Gets the ACP tool kind for the given DotCraft tool name.
    /// </summary>
    public static string GetKind(string? toolName, string? declaredKind = null)
    {
        if (!string.IsNullOrWhiteSpace(declaredKind))
            return declaredKind.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(toolName))
            return AcpToolKind.Other;

        return ToolKindMap.GetValueOrDefault(toolName!, AcpToolKind.Other);
    }

    /// <summary>
    /// Extracts file paths from tool call arguments for reporting file locations.
    /// </summary>
    public static List<string>? ExtractFilePaths(string toolName, IDictionary<string, object?>? arguments)
    {
        if (arguments == null) return null;

        var kind = GetKind(toolName);
        if (kind is not (AcpToolKind.Read or AcpToolKind.Edit or AcpToolKind.Delete))
            return null;

        var paths = new List<string>();
        foreach (var key in new[] { "path", "filePath", "file_path", "file" })
        {
            if (arguments.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                paths.Add(s);
        }

        return paths.Count > 0 ? paths : null;
    }
}
