namespace DotCraft.Workspaces;

/// <summary>
/// Provides the normalized paths owned by one DotCraft runtime.
/// </summary>
public sealed class DotCraftPaths
{
    internal DotCraftPaths(string workspacePath, string dataPath, string? userDataPath)
    {
        WorkspacePath = workspacePath;
        Data = new DotCraftPathRoot(dataPath);
        UserData = new OptionalDotCraftPathRoot(userDataPath);
    }

    /// <summary>Gets the normalized workspace root.</summary>
    public string WorkspacePath { get; }

    /// <summary>Gets the required workspace-owned data root.</summary>
    public DotCraftPathRoot Data { get; }

    /// <summary>Gets the optional user-owned data root.</summary>
    public OptionalDotCraftPathRoot UserData { get; }
}

/// <summary>Provides safe path composition beneath a normalized data root.</summary>
public sealed class DotCraftPathRoot
{
    internal DotCraftPathRoot(string rootPath)
    {
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    /// <summary>Gets the normalized root directory.</summary>
    public string RootPath { get; }

    /// <summary>Resolves a path beneath this root.</summary>
    public string Resolve(params string[] relativeParts)
    {
        ArgumentNullException.ThrowIfNull(relativeParts);
        if (relativeParts.Length == 0)
            return RootPath;

        var candidate = RootPath;
        foreach (var part in relativeParts)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(part);
            if (Path.IsPathRooted(part))
                throw new ArgumentException("Path parts must be relative to the DotCraft data root.", nameof(relativeParts));
            candidate = Path.Combine(candidate, part);
        }

        var resolved = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(RootPath, resolved);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Resolved path must remain inside the DotCraft data root.", nameof(relativeParts));
        }

        EnsureExistingLinksRemainInsideRoot(resolved);

        return resolved;
    }

    private void EnsureExistingLinksRemainInsideRoot(string resolved)
    {
        var relative = Path.GetRelativePath(RootPath, resolved);
        var current = RootPath;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) == 0)
                continue;

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target == null)
                continue;
            var targetRelative = Path.GetRelativePath(RootPath, Path.GetFullPath(target.FullName));
            if (Path.IsPathRooted(targetRelative)
                || targetRelative == ".."
                || targetRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || targetRelative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Resolved path must not traverse a link outside the DotCraft data root.",
                    nameof(resolved));
            }
        }
    }
}

/// <summary>
/// Represents an optional user-owned data root without selecting a hidden default directory.
/// </summary>
public sealed class OptionalDotCraftPathRoot
{
    private readonly DotCraftPathRoot? _root;

    internal OptionalDotCraftPathRoot(string? rootPath)
    {
        _root = string.IsNullOrWhiteSpace(rootPath) ? null : new DotCraftPathRoot(rootPath);
    }

    /// <summary>Gets whether a user-owned data root was configured by the host.</summary>
    public bool IsConfigured => _root is not null;

    /// <summary>Gets the normalized root directory, or <see langword="null"/> when disabled.</summary>
    public string? RootPath => _root?.RootPath;

    /// <summary>Resolves a user-owned path, or returns <see langword="null"/> when disabled.</summary>
    public string? ResolveOrNull(params string[] relativeParts) => _root?.Resolve(relativeParts);

    /// <summary>Returns the configured user-owned root or throws a consistent persistence error.</summary>
    public DotCraftPathRoot Require(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return _root ?? throw new InvalidOperationException(
            $"UserDataPath is required for {operation}.");
    }
}
