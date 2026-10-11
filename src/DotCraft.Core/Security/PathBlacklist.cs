namespace DotCraft.Security;

public sealed class PathBlacklist
{
    private readonly List<string> _normalizedPaths;

    public PathBlacklist(IEnumerable<string> blacklistedPaths)
    {
        _normalizedPaths = blacklistedPaths
            .Select(NormalizePath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    public bool IsBlacklisted(string fullPath)
    {
        if (_normalizedPaths.Count == 0)
            return false;

        var normalized = NormalizePath(fullPath);
        foreach (var blocked in _normalizedPaths)
        {
            if (normalized.Equals(blocked, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(blocked + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public bool HasEntryWithin(string directoryPath)
    {
        var normalized = NormalizePath(directoryPath);
        var prefix = Path.EndsInDirectorySeparator(normalized) ? normalized : normalized + Path.DirectorySeparatorChar;
        return _normalizedPaths.Any(blocked =>
            blocked.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            blocked.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (expanded.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            expanded = Path.Combine(home, expanded[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        try
        {
            return Path.GetFullPath(expanded);
        }
        catch
        {
            return expanded;
        }
    }
}
