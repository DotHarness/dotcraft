using System.Text;
using System.Text.RegularExpressions;

namespace DotCraft.SessionImport;

internal static class ImportTextRewrite
{
    private const string WordBefore = "(?<![A-Za-z0-9_])";
    private const string WordAfter = "(?![A-Za-z0-9_])";
    /// <summary>Paths, packages, and domains keep the source name, so a brand next to them is not prose.</summary>
    private const string BrandBefore = @"(?<![A-Za-z0-9_./\\~@-])";
    private const string BrandAfter = @"(?![A-Za-z0-9_/\\]|[.-][A-Za-z0-9])";

    private static readonly Regex Code = new(@"(?ms)^[ \t]*(```|~~~).*?(?:^[ \t]*\1[^\n]*$|\z)|`[^`\n]+`",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public static string Apply(string text, string source)
    {
        var document = source switch { "claude-code" => "CLAUDE.md", "cursor" => ".cursorrules", _ => "AGENTS.md" };
        text = Replace(text, WordBefore, Regex.Escape(document), WordAfter, "AGENTS.md", RegexOptions.IgnoreCase);
        var (brand, options) = source switch
        {
            "claude-code" => ("claude code|claude-code|claude_code|claudecode|claude", RegexOptions.IgnoreCase),
            "cursor" => ("Cursor", RegexOptions.None),
            "codex" => ("Codex", RegexOptions.None),
            _ => ((string?)null, RegexOptions.None)
        };
        return brand == null ? text : ProseOnly(text, prose => Replace(prose, BrandBefore, brand, BrandAfter, "DotCraft", options));
    }

    private static string ProseOnly(string text, Func<string, string> rewrite)
    {
        var result = new StringBuilder(text.Length);
        var last = 0;
        foreach (Match code in Code.Matches(text))
        {
            result.Append(rewrite(text[last..code.Index])).Append(code.Value);
            last = code.Index + code.Length;
        }
        return result.Append(rewrite(text[last..])).ToString();
    }

    private static string Replace(string text, string before, string pattern, string after, string value, RegexOptions options) =>
        Regex.Replace(text, before + "(?:" + pattern + ")" + after, value,
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
