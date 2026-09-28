using System.Text.RegularExpressions;

namespace DotCraft.SessionImport;

internal static class ImportTextRewrite
{
    public static string Apply(string text, string source)
    {
        var document = source switch { "claude-code" => "CLAUDE.md", "cursor" => ".cursorrules", _ => "AGENTS.md" };
        text = Replace(text, Regex.Escape(document), "AGENTS.md", RegexOptions.IgnoreCase);
        return source switch
        {
            "claude-code" => Replace(text, "claude code|claude-code|claude_code|claudecode|claude", "DotCraft", RegexOptions.IgnoreCase),
            "cursor" => Replace(text, "Cursor", "DotCraft", RegexOptions.None),
            "codex" => Replace(text, "Codex", "DotCraft", RegexOptions.None),
            _ => text
        };
    }

    private static string Replace(string text, string pattern, string value, RegexOptions options) =>
        Regex.Replace(text, "(?<![A-Za-z0-9_])(?:" + pattern + ")(?![A-Za-z0-9_])", value,
            options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static bool SupportedCommand(string text)
    {
        if (Regex.IsMatch(text, @"!`|\$ARGUMENTS\[|\$\{(?:CLAUDE|CURSOR)_", RegexOptions.CultureInvariant)) return false;
        if (!text.StartsWith("---", StringComparison.Ordinal)) return true;
        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return false;
        return !Regex.IsMatch(text[3..end], @"(?m)^\s*(allowed-tools|disallowed-tools|model|context|agent|hooks|disable-model-invocation)\s*:");
    }
}
