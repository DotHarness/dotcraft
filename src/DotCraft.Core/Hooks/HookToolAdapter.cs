using System.Collections;
using System.Text.RegularExpressions;

namespace DotCraft.Hooks;

internal sealed record HookToolView(
    string? NativeName,
    string? HookName,
    IReadOnlyList<string> Aliases,
    Dictionary<string, object?> ToolInput,
    string MatchText);

internal static class HookToolAdapter
{
    public static HookToolView Build(string? nativeName, object? args)
    {
        var dict = ToDictionary(args);
        var hookName = ToHookName(nativeName);
        var aliases = BuildAliases(nativeName, hookName);
        var input = ToPortableInput(hookName, dict);
        return new HookToolView(nativeName, hookName, aliases, input, BuildMatchText(hookName, input));
    }

    public static bool MatchesMatcher(string matcher, HookToolView tool)
    {
        if (string.IsNullOrEmpty(matcher))
            return true;

        if (tool.Aliases.Count == 0)
            return true;

        try
        {
            return tool.Aliases.Any(alias => Regex.IsMatch(alias, matcher, RegexOptions.IgnoreCase));
        }
        catch (RegexParseException)
        {
            return false;
        }
    }

    public static bool MatchesCondition(string? condition, HookToolView tool)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var match = Regex.Match(condition.Trim(), @"^(?<tool>[A-Za-z0-9_.-]+)\((?<pattern>.*)\)$");
        if (!match.Success)
            return false;

        var toolName = match.Groups["tool"].Value;
        if (!tool.Aliases.Any(alias => string.Equals(alias, toolName, StringComparison.OrdinalIgnoreCase)))
            return false;

        var pattern = match.Groups["pattern"].Value.Trim();
        if (string.IsNullOrEmpty(pattern))
            return true;

        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            var prefix = pattern[..^2].TrimEnd(':').Trim();
            return tool.MatchText.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return WildcardMatches(tool.MatchText, pattern);
    }

    private static string? ToHookName(string? nativeName)
    {
        if (string.IsNullOrWhiteSpace(nativeName))
            return null;

        var name = nativeName.Trim();
        return name switch
        {
            "Exec" or "ShellTools_Exec" or "SandboxShellTools_Exec" => "Bash",
            "WriteFile" or "FileTools_WriteFile" => "Write",
            "EditFile" or "FileTools_EditFile" => "Edit",
            _ when name.Contains("ShellTools_Exec", StringComparison.OrdinalIgnoreCase) => "Bash",
            _ when name.Contains("WriteFile", StringComparison.OrdinalIgnoreCase) => "Write",
            _ when name.Contains("EditFile", StringComparison.OrdinalIgnoreCase) => "Edit",
            _ => name
        };
    }

    private static IReadOnlyList<string> BuildAliases(string? nativeName, string? hookName)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(nativeName))
            aliases.Add(nativeName.Trim());
        if (!string.IsNullOrWhiteSpace(hookName))
            aliases.Add(hookName.Trim());

        switch (hookName)
        {
            case "Bash":
                aliases.Add("Shell");
                aliases.Add("Exec");
                break;
            case "Write":
                aliases.Add("WriteFile");
                break;
            case "Edit":
                aliases.Add("EditFile");
                break;
        }

        return aliases.ToList();
    }

    private static Dictionary<string, object?> ToPortableInput(string? hookName, Dictionary<string, object?> args)
    {
        var input = new Dictionary<string, object?>(args, StringComparer.Ordinal);
        switch (hookName)
        {
            case "Bash":
                CopyAlias(args, input, "command", "command");
                CopyAlias(args, input, "cwd", "cwd");
                CopyAlias(args, input, "shell", "shell");
                break;
            case "Write":
                CopyAlias(args, input, "path", "file_path");
                CopyAlias(args, input, "filePath", "file_path");
                CopyAlias(args, input, "content", "content");
                break;
            case "Edit":
                CopyAlias(args, input, "path", "file_path");
                CopyAlias(args, input, "filePath", "file_path");
                CopyAlias(args, input, "oldText", "old_string");
                CopyAlias(args, input, "newText", "new_string");
                CopyAlias(args, input, "replaceAll", "replace_all");
                break;
        }

        return input;
    }

    private static string BuildMatchText(string? hookName, IReadOnlyDictionary<string, object?> input)
    {
        if (hookName == "Bash" && input.TryGetValue("command", out var command) && command != null)
            return Convert.ToString(command, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        var parts = new List<string>();
        foreach (var key in new[] { "file_path", "path", "content", "new_string", "old_string", "command" })
        {
            if (input.TryGetValue(key, out var value) && value != null)
                parts.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return string.Join("\n", parts);
    }

    private static Dictionary<string, object?> ToDictionary(object? args)
    {
        if (args is Dictionary<string, object?> existing)
            return new Dictionary<string, object?>(existing, StringComparer.Ordinal);

        if (args is IReadOnlyDictionary<string, object?> readOnly)
            return new Dictionary<string, object?>(readOnly, StringComparer.Ordinal);

        if (args is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key)
                    result[key] = entry.Value;
            }

            return result;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private static void CopyAlias(
        IReadOnlyDictionary<string, object?> source,
        Dictionary<string, object?> target,
        string from,
        string to)
    {
        if (target.ContainsKey(to))
            return;

        if (source.TryGetValue(from, out var value))
            target[to] = value;
    }

    private static bool WildcardMatches(string value, string wildcard)
    {
        var pattern = "^" + Regex.Escape(wildcard).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }
}
