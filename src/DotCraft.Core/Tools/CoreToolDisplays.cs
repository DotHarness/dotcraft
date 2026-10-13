using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Diagnostics;

namespace DotCraft.Tools;

/// <summary>
/// Human-readable display formatters for core tool calls.
/// Each method is referenced by the corresponding tool via <see cref="ToolAttribute.DisplayMethod"/>.
/// </summary>
public static class CoreToolDisplays
{
    public static string ReadFile(IDictionary<string, object?>? args)
    {
        var path = ToolDisplayHelpers.GetString(args, "path") ?? "?";
        var offset = ToolDisplayHelpers.GetInt(args, "offset");
        var limit = ToolDisplayHelpers.GetInt(args, "limit");

        if (offset > 0 && limit > 0)
            return $"Read {path} lines {offset}-{offset + limit - 1}";
        if (offset > 0)
            return $"Read {path} from line {offset}";
        return $"Read {path}";
    }

    public static string WriteFile(IDictionary<string, object?>? args)
        => $"Wrote {ToolDisplayHelpers.GetString(args, "path") ?? "file"}";

    public static string EditFile(IDictionary<string, object?>? args)
    {
        var path = ToolDisplayHelpers.GetString(args, "path") ?? "file";
        var replaceAll = ToolDisplayHelpers.GetBool(args, "replaceAll");

        // For search/replace, show the first meaningful line of oldText as a content hint
        // so multiple edits to the same file are visually distinguishable.
        var oldText = ToolDisplayHelpers.GetString(args, "oldText");
        if (!string.IsNullOrWhiteSpace(oldText))
        {
            var firstLine = oldText.Replace("\r\n", "\n").Split('\n')
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(firstLine))
            {
                var hint = $"Edited {path}: \"{ToolDisplayHelpers.Truncate(firstLine, 40)}\"";
                return replaceAll ? $"{hint} (replace all)" : hint;
            }
        }

        return replaceAll ? $"Edited {path} (replace all)" : $"Edited {path}";
    }

    public static string GrepFiles(IDictionary<string, object?>? args)
    {
        var pattern = ToolDisplayHelpers.GetString(args, "pattern") ?? "?";
        var path = ToolDisplayHelpers.GetString(args, "path");
        var include = ToolDisplayHelpers.GetString(args, "include");

        var desc = $"Grepped \"{ToolDisplayHelpers.Truncate(pattern, 60)}\"";
        if (!string.IsNullOrEmpty(path))
            desc += $" in {path}";
        if (!string.IsNullOrEmpty(include))
            desc += $" ({include})";
        return desc;
    }

    public static string FindFiles(IDictionary<string, object?>? args)
    {
        var pattern = ToolDisplayHelpers.GetString(args, "pattern") ?? "*";
        var path = ToolDisplayHelpers.GetString(args, "path");

        var desc = $"Found files \"{pattern}\"";
        if (!string.IsNullOrEmpty(path))
            desc += $" in {path}";
        return desc;
    }

    public static string Exec(IDictionary<string, object?>? args)
        => $"{ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "command") ?? "command", 80)}";

    public static string LSP(IDictionary<string, object?>? args)
    {
        var operation = ToolDisplayHelpers.GetString(args, "operation") ?? "operation";
        var path = ToolDisplayHelpers.GetString(args, "filePath") ?? "?";
        var line = ToolDisplayHelpers.GetInt(args, "line");
        var character = ToolDisplayHelpers.GetInt(args, "character");
        return $"LSP {operation} {path}:{line}:{character}";
    }

    public static string SearchTools(IDictionary<string, object?>? args)
        => $"Searched tools: \"{ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "query") ?? "", 60)}\"";

    public static string WebSearch(IDictionary<string, object?>? args)
        => $"Searched \"{ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "query") ?? "", 80)}\"";

    public static string WebFetch(IDictionary<string, object?>? args)
        => $"Fetched {ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "url") ?? "URL", 80)}";

    public static string RequestUserInput(IDictionary<string, object?>? args)
    {
        var count = CountRequestUserInputQuestions(args);
        if (count == 1)
            return "Ask 1 question";
        if (count > 1)
            return $"Ask {count.Value} questions";
        return "Ask questions";
    }

    private static int? CountRequestUserInputQuestions(IDictionary<string, object?>? args)
    {
        if (args == null || !args.TryGetValue("questions", out var value) || value == null)
            return null;

        return value switch
        {
            JsonArray jsonArray when jsonArray.Count > 0 => jsonArray.Count,
            JsonElement { ValueKind: JsonValueKind.Array } element when element.GetArrayLength() > 0 => element.GetArrayLength(),
            ICollection collection when collection.Count > 0 => collection.Count,
            IEnumerable enumerable when value is not string => CountEnumerable(enumerable),
            _ => null
        };
    }

    private static int? CountEnumerable(IEnumerable enumerable)
    {
        var count = 0;
        foreach (var _ in enumerable)
        {
            count++;
            if (count > 100)
                break;
        }

        return count > 0 ? count : null;
    }

    public static string SpawnAgent(IDictionary<string, object?>? args)
    {
        var nickname = ToolDisplayHelpers.GetString(args, "agentNickname");
        var prompt = ToolDisplayHelpers.GetString(args, "message")
            ?? ToolDisplayHelpers.GetString(args, "agentPrompt")
            ?? ToolDisplayHelpers.GetString(args, "taskName")
            ?? "task";
        return $"Spawned subagent: {SubAgentManager.NormalizeLabel(nickname, prompt)}";
    }

    public static string CreatePlan(IDictionary<string, object?>? args)
    {
        var plan = ToolDisplayHelpers.GetString(args, "plan");
        var title = string.IsNullOrWhiteSpace(plan)
            ? ToolDisplayHelpers.GetString(args, "title")
            : PlanMarkdownParser.Parse(plan).Title;
        return $"Created plan: {ToolDisplayHelpers.Truncate(title ?? "plan", 60)}";
    }

    public static string UpdateTodos(IDictionary<string, object?>? args)
        => "Updated plan tasks";

    public static string TodoWrite(IDictionary<string, object?>? args)
        => "Updated task list";
}
