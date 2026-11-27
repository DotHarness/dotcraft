using System.Text.Json;
using DotHarness.Acp.V1;

namespace DotCraft.Acp;

public static class AcpToolKindMapper
{
    private static readonly Dictionary<string, ToolKind> StandardKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["read"] = ToolKind.Read,
        ["edit"] = ToolKind.Edit,
        ["delete"] = ToolKind.Delete,
        ["move"] = ToolKind.Move,
        ["search"] = ToolKind.Search,
        ["execute"] = ToolKind.Execute,
        ["think"] = ToolKind.Think,
        ["fetch"] = ToolKind.Fetch,
        ["switch_mode"] = ToolKind.SwitchMode,
        ["other"] = ToolKind.Other
    };

    private static readonly Dictionary<string, ToolKind> ToolKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReadFile"] = ToolKind.Read,
        ["WriteFile"] = ToolKind.Edit,
        ["EditFile"] = ToolKind.Edit,
        ["GrepFiles"] = ToolKind.Search,
        ["FindFiles"] = ToolKind.Search,
        ["Exec"] = ToolKind.Execute,
        ["WebSearch"] = ToolKind.Fetch,
        ["WebFetch"] = ToolKind.Fetch,
        ["SpawnAgent"] = ToolKind.Think,
        ["CreatePlan"] = ToolKind.Other,
        ["TodoWrite"] = ToolKind.Other
    };

    public static ToolKind GetKind(string? toolName, string? declaredKind = null)
    {
        if (!string.IsNullOrWhiteSpace(declaredKind)
            && StandardKinds.TryGetValue(declaredKind.Trim(), out var standard))
        {
            return standard;
        }

        if (string.IsNullOrWhiteSpace(toolName))
            return ToolKind.Other;

        return ToolKindMap.GetValueOrDefault(toolName, ToolKind.Other);
    }

    public static List<string>? ExtractFilePaths(string toolName, JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;

        var kind = GetKind(toolName);
        if (kind != ToolKind.Read && kind != ToolKind.Edit && kind != ToolKind.Delete)
            return null;

        var paths = new List<string>();
        foreach (var key in new[] { "path", "filePath", "file_path", "file" })
        {
            if (arguments.TryGetProperty(key, out var val)
                && val.ValueKind == JsonValueKind.String
                && val.GetString() is { Length: > 0 } path)
            {
                paths.Add(path);
            }
        }

        return paths.Count > 0 ? paths : null;
    }
}
