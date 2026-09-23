using System.Text;

namespace DotCraft.SessionImport;

internal static class WorkspacePathMatcher
{
    private const int MaxCursorFolderProbes = 128;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var value = path.Trim();
        if (OperatingSystem.IsWindows())
        {
            if (value.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                value = @"\\" + value[8..];
            else if (value.StartsWith(@"\\?\", StringComparison.Ordinal))
                value = value[4..];
        }

        try
        {
            return Path.IsPathFullyQualified(value) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsWithin(string path, string root)
    {
        if (string.Equals(path, root, PathComparison))
            return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, PathComparison);
    }

    public static bool IsSameDirectory(string left, string right) =>
        string.Equals(Normalize(left), Normalize(right), PathComparison);

    public static string? ResolveMemberCwd(string? cwd, string workspaceRoot)
    {
        var normalized = Normalize(cwd);
        var root = Normalize(workspaceRoot);
        if (normalized is null || root is null || !IsWithin(normalized, root))
            return null;
        return Directory.Exists(normalized) ? normalized : null;
    }

    public static string CursorSlug(string path)
    {
        var builder = new StringBuilder(path.Length);
        var pendingSeparator = false;
        foreach (var ch in path)
        {
            if (!char.IsAsciiLetterOrDigit(ch))
            {
                pendingSeparator = true;
                continue;
            }

            if (pendingSeparator && builder.Length > 0)
                builder.Append('-');
            pendingSeparator = false;
            builder.Append(ch);
        }

        return builder.ToString();
    }

    public static string? ResolveCursorProjectCwd(string directoryName, string workspaceRoot)
    {
        var slug = CursorSlug(workspaceRoot);
        if (slug.Length == 0)
            return null;
        if (string.Equals(directoryName, slug, PathComparison))
            return workspaceRoot;
        if (!directoryName.StartsWith(slug + "-", PathComparison))
            return null;
        var tokens = directoryName[(slug.Length + 1)..].Split('-', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length == 0 ? null : ReconstructSubfolder(workspaceRoot, tokens);
    }

    private static string? ReconstructSubfolder(string root, string[] tokens)
    {
        var probes = 0;
        return Search(root, tokens[0], 1);

        string? Search(string parent, string segment, int next)
        {
            var closed = Path.Combine(parent, segment);
            if (next == tokens.Length)
                return Probe(closed) ? closed : null;
            if (Probe(closed) && Search(closed, tokens[next], next + 1) is { } nested)
                return nested;
            return probes < MaxCursorFolderProbes ? Search(parent, $"{segment}-{tokens[next]}", next + 1) : null;
        }

        bool Probe(string path) => probes++ < MaxCursorFolderProbes && Directory.Exists(path);
    }
}
