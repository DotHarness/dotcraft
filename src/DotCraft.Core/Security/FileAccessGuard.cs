namespace DotCraft.Security;

/// <summary>
/// Shared file-access guard used by tools and other runtime components that need
/// workspace-boundary checks, blacklist enforcement, and approval routing for
/// paths outside the workspace.
/// </summary>
public sealed class FileAccessGuard
{
    private readonly string _workspaceRoot;
    private readonly bool _requireApprovalOutsideWorkspace;
    private readonly IApprovalService? _approvalService;
    private readonly PathBlacklist? _blacklist;
    private readonly IReadOnlyList<string> _trustedReadPaths;

    /// <summary>
    /// Creates a new guard for file access decisions in a workspace.
    /// </summary>
    public FileAccessGuard(
        string workspaceRoot,
        bool requireApprovalOutsideWorkspace = true,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null,
        IReadOnlyList<string>? trustedReadPaths = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _trustedReadPaths = (trustedReadPaths ?? [])
            .Select(Path.GetFullPath)
            .ToArray();
    }

    /// <summary>
    /// Resolves a workspace-relative or absolute path to a normalized full path.
    /// </summary>
    public string ResolvePath(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(home))
        {
            if (path == "~" || path.StartsWith("~/") || path.StartsWith("~\\"))
                path = Path.Combine(home, path[1..].TrimStart('/', '\\'));

            path = path.Replace("${HOME}", home).Replace("$HOME", home);
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, expanded));
    }

    /// <summary>
    /// Validates access to a path that has already been resolved to a full path.
    /// Returns null when access is allowed, or an error string when it is denied.
    /// </summary>
    public async Task<string?> ValidatePathAsync(
        string fullPath,
        string operation,
        string originalPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_blacklist != null && _blacklist.IsBlacklisted(fullPath))
            return $"Error: Path '{originalPath}' is in the blacklist and cannot be accessed.";

        var isWithinWorkspace = IsWithinBoundary(fullPath, _workspaceRoot);
        if (isWithinWorkspace)
            return null;

        if ((operation is "read" or "list") && IsWithinTrustedReadPath(fullPath))
            return null;

        if (!_requireApprovalOutsideWorkspace)
            return $"Error: Path '{originalPath}' is outside workspace boundary.";

        if (_approvalService != null)
        {
            var context = ApprovalContextScope.Current;
            var approved = await _approvalService.RequestFileApprovalAsync(operation, fullPath, context);
            if (!approved)
                return $"Error: Operation '{operation}' on '{originalPath}' was rejected by user.";
        }

        return null;
    }

    private bool IsWithinTrustedReadPath(string fullPath)
    {
        foreach (var trustedPath in _trustedReadPaths)
        {
            if (IsWithinBoundary(fullPath, trustedPath))
                return true;
        }

        return false;
    }

    private static bool IsWithinBoundary(string fullPath, string boundaryRoot)
    {
        var resolvedPath = ResolveSymbolicLinkSafe(fullPath);
        var resolvedBoundary = ResolveSymbolicLinkSafe(boundaryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (resolvedPath.Equals(resolvedBoundary, StringComparison.OrdinalIgnoreCase))
            return true;

        return resolvedPath.StartsWith(resolvedBoundary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || resolvedPath.StartsWith(resolvedBoundary + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSymbolicLinkSafe(string path)
    {
        try
        {
            return ResolveSymbolicLink(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Path.GetFullPath(path);
        }
    }

    private const int MaxSymlinkResolveDepth = 64;

    private static string ResolveSymbolicLink(string path)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ResolveSymbolicLinkCore(Path.GetFullPath(path), visited, depth: 0);
    }

    private static string ResolveSymbolicLinkCore(string path, HashSet<string> visited, int depth)
    {
        if (depth >= MaxSymlinkResolveDepth)
            throw new InvalidOperationException("Symbolic link resolution exceeded maximum depth.");

        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        if (!visited.Add(path))
            throw new InvalidOperationException("Circular symbolic link detected.");

        try
        {
            FileSystemInfo info = File.Exists(path)
                ? new FileInfo(path)
                : new DirectoryInfo(path);

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: false);
                if (target != null)
                {
                    var resolved = Path.GetFullPath(target.FullName);
                    return ResolveSymbolicLinkCore(resolved, visited, depth + 1);
                }
            }

            return path;
        }
        catch (PlatformNotSupportedException)
        {
            return path;
        }
    }
}
