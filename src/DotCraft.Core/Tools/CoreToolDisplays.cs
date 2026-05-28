using System.Collections;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Formats the unified JSON result from WebSearch into human-readable lines.
    /// Returns null if the result is empty or unparseable (caller falls back to generic truncation).
    /// </summary>
    public static IReadOnlyList<string>? WebSearchResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            // Handle double-encoded JSON from clients that wrap string results in quotes.
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (inner == null) return null;
                using var innerDoc = JsonDocument.Parse(inner);
                return ParseWebSearchResult(innerDoc.RootElement);
            }

            return ParseWebSearchResult(root);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseWebSearchResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        // Error shape
        if (root.TryGetProperty("error", out var errorProp))
        {
            var msg = errorProp.GetString();
            return string.IsNullOrWhiteSpace(msg) ? null : [$"Error: {msg}"];
        }

        if (!root.TryGetProperty("results", out var resultsProp) ||
            resultsProp.ValueKind != JsonValueKind.Array)
            return null;

        var lines = new List<string>();
        var count = resultsProp.GetArrayLength();

        if (count == 0)
        {
            lines.Add("No results found.");
            return lines;
        }

        lines.Add($"{count} result{(count == 1 ? "" : "s")}:");

        var i = 1;
        foreach (var item in resultsProp.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;

            string domain = "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try { domain = new Uri(url).Host; }
                catch { domain = url; }
            }

            var titleText = ToolDisplayHelpers.Truncate(title ?? url ?? "?", 70);
            var line = string.IsNullOrWhiteSpace(domain)
                ? $"{i}. {titleText}"
                : $"{i}. {titleText} — {domain}";
            lines.Add(line);
            i++;
        }

        return lines;
    }

    /// <summary>
    /// Formats the unified JSON result from WebFetch into a single human-readable line.
    /// Returns null if the result is empty or unparseable.
    /// </summary>
    public static IReadOnlyList<string>? WebFetchResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (inner == null) return null;
                using var innerDoc = JsonDocument.Parse(inner);
                return ParseWebFetchResult(innerDoc.RootElement);
            }

            return ParseWebFetchResult(root);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseWebFetchResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("error", out var errorProp))
        {
            var msg = errorProp.GetString();
            return string.IsNullOrWhiteSpace(msg) ? null : [$"Error: {msg}"];
        }

        var parts = new List<string>();

        if (root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Number)
            parts.Add(statusProp.GetInt32().ToString());

        if (root.TryGetProperty("length", out var lenProp) && lenProp.ValueKind == JsonValueKind.Number)
            parts.Add($"{lenProp.GetInt64():N0} chars");

        if (root.TryGetProperty("extractor", out var extProp))
        {
            var ext = extProp.GetString();
            if (!string.IsNullOrWhiteSpace(ext)) parts.Add(ext);
        }

        if (root.TryGetProperty("truncated", out var truncProp) &&
            truncProp.ValueKind == JsonValueKind.True)
            parts.Add("truncated");

        return parts.Count == 0 ? null : [string.Join(" · ", parts)];
    }

    /// <summary>
    /// Summarizes a ReadFile result: line count, paginated range, or directory entry count.
    /// </summary>
    public static IReadOnlyList<string>? ReadFileResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];

        // Multimodal ReadFile (image): DescribeResult text starts with summary + binary line
        if (result.StartsWith("Image:", StringComparison.Ordinal) &&
            result.Contains("[Image (", StringComparison.Ordinal))
            return ["Image file (vision input)"];

        // Directory listing: lines prefixed with [DIR] or [FILE]
        var lines = result.Split('\n');
        var dirCount = lines.Count(l => l.StartsWith("[DIR] ", StringComparison.Ordinal));
        var fileCount = lines.Count(l => l.StartsWith("[FILE] ", StringComparison.Ordinal));
        if (dirCount + fileCount > 0)
        {
            var parts = new List<string>();
            if (dirCount > 0) parts.Add($"{dirCount} dir{(dirCount == 1 ? "" : "s")}");
            if (fileCount > 0) parts.Add($"{fileCount} file{(fileCount == 1 ? "" : "s")}");
            return [string.Join(", ", parts)];
        }

        // Paginated read: look for the trailer line appended by ReadFile
        var trailer = lines.FirstOrDefault(l => l.TrimStart().StartsWith("(Showing lines ", StringComparison.Ordinal));
        if (trailer != null)
        {
            // "(Showing lines N-M of T. Use offset=...)"
            var match = System.Text.RegularExpressions.Regex.Match(trailer, @"Showing lines (\d+)-(\d+) of (\d+)");
            if (match.Success)
                return [$"Lines {match.Groups[1].Value}-{match.Groups[2].Value} of {match.Groups[3].Value}"];
        }

        var endTrailer = lines.FirstOrDefault(l => l.TrimStart().StartsWith("(End of file - total ", StringComparison.Ordinal));
        if (endTrailer != null)
        {
            var match = System.Text.RegularExpressions.Regex.Match(endTrailer, @"total (\d+) lines");
            if (match.Success)
                return [$"{match.Groups[1].Value} lines"];
        }

        // Plain full-file read: count content lines
        var lineCount = lines.Length;
        return [$"{lineCount} line{(lineCount == 1 ? "" : "s")}"];
    }

    /// <summary>
    /// Summarizes a WriteFile result: byte and line count on success, or the error message.
    /// </summary>
    public static IReadOnlyList<string>? WriteFileResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];

        // "Successfully wrote N bytes (M lines) to path"
        var enrichedMatch = Regex.Match(result, @"wrote (\d+) bytes \((\d+) lines\)");
        if (enrichedMatch.Success)
        {
            var bytes = long.Parse(enrichedMatch.Groups[1].Value);
            var lines = int.Parse(enrichedMatch.Groups[2].Value);
            return [$"{bytes:N0} bytes, {lines} lines"];
        }

        // Legacy format: "Successfully wrote N bytes to path"
        var legacyMatch = Regex.Match(result, @"wrote (\d+) bytes");
        if (legacyMatch.Success)
        {
            var bytes = long.Parse(legacyMatch.Groups[1].Value);
            return [$"{bytes:N0} bytes"];
        }

        return [result.Split('\n')[0].Trim()];
    }

    /// <summary>
    /// Summarizes an EditFile result with location and line count change, or error message.
    /// </summary>
    public static IReadOnlyList<string>? EditFileResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];

        if (result.StartsWith("Successfully", StringComparison.OrdinalIgnoreCase))
        {
            // Replace all: "Successfully replaced N occurrences in {path}"
            var replaceAllMatch = Regex.Match(result, @"Successfully replaced (\d+) occurrences in ");
            if (replaceAllMatch.Success)
                return [$"{replaceAllMatch.Groups[1].Value} occurrences replaced"];

            // Search/replace: "Successfully edited {path} at line N (A -> B lines)"
            var srMatch = Regex.Match(result, @"at line (\d+) \((\d+) -> (\d+) lines\)");
            if (srMatch.Success)
            {
                var lineNum = int.Parse(srMatch.Groups[1].Value);
                var oldCount = int.Parse(srMatch.Groups[2].Value);
                var newCount = int.Parse(srMatch.Groups[3].Value);
                var delta = oldCount == newCount ? "±0 lines"
                    : $"{oldCount} → {newCount} lines";
                return [$"L{lineNum}, {delta}"];
            }

            // Line-range: "Successfully replaced lines N-M in {path} (A -> B lines)"
            var lrMatch = Regex.Match(result, @"replaced lines (\d+)-(\d+) in .+ \((\d+) -> (\d+) lines\)");
            if (lrMatch.Success)
            {
                var start = int.Parse(lrMatch.Groups[1].Value);
                var end = int.Parse(lrMatch.Groups[2].Value);
                var oldCount = int.Parse(lrMatch.Groups[3].Value);
                var newCount = int.Parse(lrMatch.Groups[4].Value);
                var delta = oldCount == newCount ? "±0 lines"
                    : $"{oldCount} → {newCount} lines";
                return [$"lines {start}-{end}, {delta}"];
            }

            return ["OK"];
        }

        return [ToolDisplayHelpers.Truncate(result.Split('\n')[0].Trim(), 80)];
    }

    /// <summary>
    /// When tool output was spilled to disk, returns a short CLI summary instead of a huge preview.
    /// </summary>
    private static IReadOnlyList<string>? TrySpillToolResultSummary(string? result)
    {
        if (string.IsNullOrEmpty(result)) return null;
        if (result.Contains(ToolResultProcessor.SpillPreviewMarker, StringComparison.Ordinal))
        {
            var m = Regex.Match(result, @"\((\d+)\s+lines omitted");
            if (m.Success)
                return [$"{m.Groups[1].Value} lines (preview, full output saved)"];
        }

        if (result.Contains(".craft/tool-results/", StringComparison.OrdinalIgnoreCase))
            return ["Large output (preview, full output saved)"];

        return null;
    }

    /// <summary>
    /// Summarizes a GrepFiles result: match and file counts, or "No matches".
    /// </summary>
    public static IReadOnlyList<string>? GrepFilesResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        var spillSummary = TrySpillToolResultSummary(result);
        if (spillSummary != null) return spillSummary;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];
        if (result.Equals("No matches found.", StringComparison.OrdinalIgnoreCase))
            return ["No matches"];

        // "Found N matches[...]:"
        var matchCount = System.Text.RegularExpressions.Regex.Match(result, @"Found (\d+) match");
        var lines = result.Split('\n');
        // File header lines: non-indented lines ending with ":"
        var fileCount = lines.Count(l => l.Length > 0 && !l.StartsWith(" ") && l.TrimEnd().EndsWith(':'));

        if (matchCount.Success)
        {
            var n = matchCount.Groups[1].Value;
            return fileCount > 0
                ? [$"{n} match{(n == "1" ? "" : "es")} in {fileCount} file{(fileCount == 1 ? "" : "s")}"]
                : [$"{n} match{(n == "1" ? "" : "es")}"];
        }

        return [ToolDisplayHelpers.Truncate(lines[0].Trim(), 80)];
    }

    /// <summary>
    /// Summarizes a FindFiles result: file count, or "No files".
    /// </summary>
    public static IReadOnlyList<string>? FindFilesResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];
        if (result.Equals("No files found.", StringComparison.OrdinalIgnoreCase))
            return ["No files"];

        // "Found N files[...]:"
        var match = System.Text.RegularExpressions.Regex.Match(result, @"Found (\d+) file");
        if (match.Success)
        {
            var n = int.Parse(match.Groups[1].Value);
            return [$"{n} file{(n == 1 ? "" : "s")}"];
        }

        return [ToolDisplayHelpers.Truncate(result.Split('\n')[0].Trim(), 80)];
    }

    /// <summary>
    /// Summarizes an Exec result: exit code plus a brief first-line preview.
    /// </summary>
    public static IReadOnlyList<string>? ExecResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;
        var spillSummary = TrySpillToolResultSummary(result);
        if (spillSummary != null) return spillSummary;
        if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            return [result.Split('\n')[0].Trim()];
        if (result.Equals("(no output)", StringComparison.OrdinalIgnoreCase) ||
            result.Contains("completed with no output)", StringComparison.Ordinal))
            return ["no output · exit 0"];

        var lines = result.Split('\n');

        // Detect non-zero exit code
        var exitLine = lines.LastOrDefault(l => l.TrimStart().StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase));
        var exitCode = 0;
        if (exitLine != null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(exitLine, @"Exit code:\s*(-?\d+)");
            if (m.Success) exitCode = int.Parse(m.Groups[1].Value);
        }

        var exitTag = exitLine != null ? $"exit {exitCode}" : "exit 0";

        // Pick a meaningful preview: first non-empty stdout line before any STDERR/Exit block
        var preview = lines
            .TakeWhile(l => !l.StartsWith("STDERR:", StringComparison.OrdinalIgnoreCase) &&
                            !l.TrimStart().StartsWith("Exit code:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);

        if (string.IsNullOrEmpty(preview))
            return [$"{exitTag}"];

        return [$"{exitTag} · {ToolDisplayHelpers.Truncate(preview, 80)}"];
    }

    public static string SpawnAgent(IDictionary<string, object?>? args)
    {
        var nickname = ToolDisplayHelpers.GetString(args, "agentNickname");
        var prompt = ToolDisplayHelpers.GetString(args, "agentPrompt") ?? "task";
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

    /// <summary>
    /// Summarises a SearchTools result: shows the count of discovered tools.
    /// </summary>
    public static IReadOnlyList<string>? SearchToolsResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        // First line is "Found N matching tool(s)..."
        var firstLine = result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(firstLine)) return null;

        return [firstLine];
    }

    public static IReadOnlyList<string>? LspResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        var firstLine = result.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return null;

        return [ToolDisplayHelpers.Truncate(firstLine.Trim(), 120)];
    }

}
