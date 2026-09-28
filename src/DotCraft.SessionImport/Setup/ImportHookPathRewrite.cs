using System.Text.RegularExpressions;

namespace DotCraft.SessionImport;

internal static class ImportHookPathRewrite
{
    public static string Apply(string command, string sourceRoot, string targetRoot)
    {
        var configName = Path.GetFileName(sourceRoot);
        if (command.Contains(configName + "\\hooks\\", StringComparison.Ordinal)
            || command.Contains("%CLAUDE_PROJECT_DIR%", StringComparison.Ordinal)
            || command.Contains("$env:CLAUDE_PROJECT_DIR", StringComparison.Ordinal)) return command;
        var sourceHooks = configName + "/hooks/";
        return Regex.Replace(command, "'(?<single>[^']*)'|\"(?<double>[^\"]*)\"|(?<plain>[^\\s'\";|&<>]+)", match =>
        {
            if (match.Index > 0 && command[match.Index - 1] == '=') return match.Value;
            var token = match.Groups["single"].Success ? match.Groups["single"].Value
                : match.Groups["double"].Success ? match.Groups["double"].Value : match.Groups["plain"].Value;
            var index = token.IndexOf(sourceHooks, StringComparison.Ordinal);
            if (index < 0) return match.Value;
            var prefix = token[..index];
            var suffix = token[(index + sourceHooks.Length)..];
            if (prefix.Length > 0 && !prefix.EndsWith('/') || prefix.Any(char.IsWhiteSpace)
                || suffix.Length == 0 || suffix.IndexOfAny(['\\', '$', '`', '*', '?', '[', '{', '}']) >= 0)
                return match.Value;
            var target = Path.Combine(targetRoot, "hooks", suffix).Replace('\\', '/');
            return "'" + target.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        }, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
