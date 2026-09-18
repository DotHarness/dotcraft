using System.Text.RegularExpressions;

namespace DotCraft.Security.ShellCommands;

public sealed record PathEvidence(string Original, string ResolvedFullPath, bool IsUnc);

public sealed partial class PathEvidenceScanner(WorkspaceBoundary boundary)
{
    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "/dev/null",
        "/dev/stdin",
        "/dev/stdout",
        "/dev/stderr",
        "/dev/zero",
        "/dev/random",
        "/dev/urandom",
        "/dev/tty",
        "NUL",
        "CON",
        "PRN",
        "AUX",
    };

    public IReadOnlyList<PathEvidence> ScanWords(IEnumerable<string> words) =>
        Collect(words.SelectMany(WordCandidates));

    public IReadOnlyList<PathEvidence> ScanText(string script) => Collect(TextCandidates(script));

    public IReadOnlyList<PathEvidence> OutsideWorkspace(IReadOnlyList<PathEvidence> evidence) =>
        evidence.Where(item => item.IsUnc || !boundary.Contains(item.ResolvedFullPath)).ToArray();

    private static IReadOnlyList<PathEvidence> Collect(IEnumerable<string> candidates)
    {
        var evidence = new List<PathEvidence>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                continue;

            var found = Recognize(candidate);
            if (found is not null)
                evidence.Add(found);
        }

        return evidence;
    }

    private static IEnumerable<string> WordCandidates(string word)
    {
        if (string.IsNullOrEmpty(word))
            yield break;

        yield return word;

        var equals = word.IndexOf('=');
        if (equals > 0 && equals < word.Length - 1)
            yield return word[(equals + 1)..];

        if (word[0] == '-')
        {
            var colon = word.IndexOf(':');
            if (colon > 0 && colon < word.Length - 1)
                yield return word[(colon + 1)..];
        }
    }

    private static IEnumerable<string> TextCandidates(string script)
    {
        foreach (Match match in AbsolutePathRegex().Matches(script))
            yield return match.Groups[1].Value;
        foreach (Match match in HomePathRegex().Matches(script))
            yield return match.Groups[1].Value;
        foreach (Match match in HomeVarRegex().Matches(script))
            yield return match.Value;
        foreach (Match match in WindowsDrivePathRegex().Matches(script))
            yield return match.Groups[1].Value;
        foreach (Match match in WindowsEnvVarPathRegex().Matches(script))
            yield return match.Value;
        foreach (Match match in PowerShellEnvVarPathRegex().Matches(script))
            yield return match.Value;
        foreach (Match match in UncPathRegex().Matches(script))
            yield return match.Groups[1].Value;
    }

    private static PathEvidence? Recognize(string original)
    {
        if (DeviceNames.Contains(original))
            return null;

        if (original.Length > 2 && original[0] == '\\' && original[1] == '\\')
            return new PathEvidence(original, ResolveOrKeep(original), IsUnc: true);

        var expanded = Expand(original);
        return expanded is null ? null : new PathEvidence(original, ResolveOrKeep(expanded), IsUnc: false);
    }

    private static string? Expand(string candidate)
    {
        if (candidate[0] == '~')
            return candidate.Length > 1 && !IsPathSeparator(candidate[1]) ? null : JoinHome(candidate, 1);

        if (candidate.StartsWith("$env:", StringComparison.OrdinalIgnoreCase))
            return ExpandPowerShellVariable(candidate);

        if (StartsWithHomeVariable(candidate, out var homeVariableLength))
            return JoinHome(candidate, homeVariableLength);

        if (candidate[0] == '%')
            return ExpandWindowsVariables(candidate);

        if (candidate.Length >= 3
            && char.IsAsciiLetter(candidate[0])
            && candidate[1] == ':'
            && IsPathSeparator(candidate[2]))
        {
            return candidate;
        }

        return candidate[0] == '/' ? candidate : null;
    }

    private static string? JoinHome(string candidate, int prefixLength) =>
        HomeDirectory() is { } home ? Join(home, candidate[prefixLength..]) : null;

    private static string Join(string basePath, string relative)
    {
        var rest = relative.TrimStart('/', '\\');
        return rest.Length == 0 ? basePath : Path.Combine(basePath, rest);
    }

    private static bool StartsWithHomeVariable(string candidate, out int length)
    {
        length = candidate.StartsWith("${HOME}", StringComparison.Ordinal) ? 7
            : candidate.StartsWith("$HOME", StringComparison.Ordinal) ? 5
            : 0;
        return length > 0 && (candidate.Length == length || IsPathSeparator(candidate[length]));
    }

    private static string? ExpandWindowsVariables(string candidate)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(candidate);
            return string.Equals(expanded, candidate, StringComparison.Ordinal) ? null : expanded;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExpandPowerShellVariable(string candidate)
    {
        var match = PowerShellEnvVarPathRegex().Match(candidate);
        if (!match.Success || match.Index != 0)
            return null;

        var name = match.Groups[1].Value;
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
            return null;

        return Join(value, candidate[(match.Index + "$env:".Length + name.Length)..]);
    }

    private static string? HomeDirectory()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? null : home;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveOrKeep(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static bool IsPathSeparator(char value) => value is '/' or '\\';

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(\/[a-zA-Z0-9._+@-]+(?:\/[a-zA-Z0-9._+@~-]*)*)", RegexOptions.Compiled)]
    private static partial Regex AbsolutePathRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(~(?:\/[a-zA-Z0-9._+@-]*)+)", RegexOptions.Compiled)]
    private static partial Regex HomePathRegex();

    [GeneratedRegex(@"(?:\$HOME|\$\{HOME\})(?:\/[a-zA-Z0-9._+@/-]*)?", RegexOptions.Compiled)]
    private static partial Regex HomeVarRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])([A-Za-z]:[\\/][^\s;|&><""']*)", RegexOptions.Compiled)]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(@"%[A-Za-z_][A-Za-z0-9_]*%(?:[\\/_][^\s;|&><""']*)?", RegexOptions.Compiled)]
    private static partial Regex WindowsEnvVarPathRegex();

    [GeneratedRegex(@"\$env:([A-Za-z_][A-Za-z0-9_]*)(?:[\\\/][^\s;|&><""']*)?", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellEnvVarPathRegex();

    [GeneratedRegex(@"(?:^|[\s;|&><(=""'])(\\\\[^\s;|&><""']+)", RegexOptions.Compiled)]
    private static partial Regex UncPathRegex();
}
