using System.Text.RegularExpressions;

namespace DotCraft.Configuration;

public static partial class ModelCapabilityIdentity
{
    public static string? ForMatching(string? model) => model is null ? null : ClaudeVersion().Replace(
        model.Trim(), match => match.Value.Replace('.', '-'));

    public static bool MatchesPrefix(string model, string pattern)
    {
        pattern = ForMatching(pattern)!;
        if (pattern.Length == 0) return false;
        if (!model.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)) return false;
        if (!pattern.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) || !char.IsDigit(pattern[^1])) return true;
        var suffix = model[pattern.Length..];
        if (suffix.Length == 0) return true;
        if (char.IsDigit(suffix[0]) || suffix[0] == '.') return false;
        if (suffix.Length > 1 && suffix[0] == '-' && char.IsDigit(suffix[1]))
            return SnapshotSuffix().IsMatch(suffix);
        return true;
    }

    [GeneratedRegex(@"claude-(?:opus|sonnet|haiku|fable|mythos)-\d+(?:\.\d+)+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaudeVersion();
    [GeneratedRegex(@"^-\d{8}(?:$|[^\d])", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotSuffix();
}
