namespace DotCraft.Security;

public sealed class WorkspaceBoundary
{
    private readonly string[] _roots;

    public WorkspaceBoundary(IEnumerable<string> roots)
    {
        _roots = roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .ToArray();

        if (_roots.Length == 0)
            throw new ArgumentException("A workspace boundary needs at least one root.", nameof(roots));
    }

    public bool Contains(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return _roots.Any(root => IsWithin(fullPath, root));
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWithin(string fullPath, string root)
    {
        var resolvedPath = ResolveSymbolicLink(fullPath);
        var resolvedRoot = ResolveSymbolicLink(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (resolvedPath.Equals(resolvedRoot, StringComparison.OrdinalIgnoreCase))
            return true;

        return resolvedPath.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || resolvedPath.StartsWith(resolvedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveSymbolicLink(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException("Cannot resolve linked path.");
                current = target.FullName;
            }
        }
        return Path.GetFullPath(current);
    }
}
