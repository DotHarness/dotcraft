namespace DotCraft.SessionImport;

internal static class SessionFileWindow
{
    public static readonly EnumerationOptions TopLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.None,
        MatchType = MatchType.Simple
    };

    public static readonly EnumerationOptions Recursive = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        MatchType = MatchType.Simple
    };

    public static IReadOnlyList<string> Files(string directory, string pattern, EnumerationOptions options)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, pattern, options).ToArray() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<string> Directories(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateDirectories(directory, "*", TopLevel).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static IReadOnlyList<(string Path, DateTimeOffset ModifiedAt)> SelectNewest(
        IEnumerable<string> paths,
        SessionImportScope scope)
    {
        var cutoff = scope.Now - scope.MaxAge;
        var selected = new List<(string Path, DateTimeOffset ModifiedAt)>();
        foreach (var path in paths)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length == 0)
                continue;
            var modifiedAt = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (modifiedAt >= cutoff)
                selected.Add((info.FullName, modifiedAt));
        }

        return selected
            .OrderByDescending(static entry => entry.ModifiedAt)
            .ThenBy(static entry => entry.Path, StringComparer.Ordinal)
            .Take(scope.MaxSessions)
            .ToArray();
    }
}
