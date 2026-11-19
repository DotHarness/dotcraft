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

    public static StringComparer Comparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string? ResolveExistingDirectory(string? path)
    {
        var normalized = Normalize(path);
        if (normalized is null || !Directory.Exists(normalized)) return null;
        try
        {
            var root = Path.GetPathRoot(normalized)!;
            var current = root;
            foreach (var segment in normalized[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                var directory = new DirectoryInfo(Path.Combine(current, segment));
                current = directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? directory.FullName;
            }
            return Normalize(current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool IsWithin(string path, string root)
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

    public static string? ResolveHostCursorProjectCwd(string directoryName, IReadOnlyList<string> roots)
    {
        var matches = new HashSet<string>(Comparer);
        foreach (var root in roots)
        {
            var slug = CursorSlug(root);
            if (slug.Length == 0) continue;
            if (string.Equals(directoryName, slug, PathComparison))
            {
                Add(root);
                continue;
            }
            if (!directoryName.StartsWith(slug + "-", PathComparison)) continue;
            var tokens = directoryName[(slug.Length + 1)..].Split('-', StringSplitOptions.RemoveEmptyEntries);
            var probes = 0;
            if (tokens.Length > 0)
                Search(root, tokens[0], 1);
            if (probes > MaxCursorFolderProbes || matches.Count > 1) return null;

            void Search(string parent, string segment, int next)
            {
                if (probes++ >= MaxCursorFolderProbes || matches.Count > 1) return;
                var candidate = Path.Combine(parent, segment);
                if (Directory.Exists(candidate))
                {
                    if (next == tokens.Length) Add(candidate);
                    else Search(candidate, tokens[next], next + 1);
                }
                if (next < tokens.Length) Search(parent, $"{segment}-{tokens[next]}", next + 1);
            }
        }
        return matches.Count == 1 ? matches.Single() : null;

        void Add(string path)
        {
            if (ResolveExistingDirectory(path) is { } existing) matches.Add(existing);
        }
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
