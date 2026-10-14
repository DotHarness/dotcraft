namespace DotCraft.Security;

/// <summary>
/// Shared file-access guard used by tools and other runtime components that need
/// workspace-boundary checks, blacklist enforcement, and approval routing for
/// paths outside the workspace.
/// </summary>
public sealed class FileAccessGuard
{
    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<string> _workspaceRoots;
    private readonly bool _requireApprovalOutsideWorkspace;
    private readonly IApprovalService? _approvalService;
    private readonly PathBlacklist? _blacklist;
    private readonly IReadOnlyList<string> _trustedReadPaths;
    private readonly IReadOnlyList<string> _trustedPaths;

    /// <summary>
    /// Creates a new guard for file access decisions in a workspace.
    /// <paramref name="trustedPaths"/> are open to every operation outside the workspace boundary.
    /// </summary>
    public FileAccessGuard(
        string workspaceRoot,
        bool requireApprovalOutsideWorkspace = true,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null,
        IReadOnlyList<string>? trustedReadPaths = null,
        IReadOnlyList<string>? workspaceRoots = null,
        IReadOnlyList<string>? trustedPaths = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _workspaceRoots = (workspaceRoots ?? [_workspaceRoot])
            .Select(Path.GetFullPath)
            .ToArray();
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _trustedReadPaths = (trustedReadPaths ?? [])
            .Select(Path.GetFullPath)
            .ToArray();
        _trustedPaths = (trustedPaths ?? [])
            .Select(Path.GetFullPath)
            .ToArray();
    }

    /// <summary>
    /// Resolves a workspace-relative or absolute path to a normalized full path.
    /// </summary>
    public string ResolvePath(string path) => ResolvePath(_workspaceRoot, path);

    /// <summary>Expands the home directory and environment variables, then resolves against <paramref name="workspaceRoot"/>.</summary>
    public static string ResolvePath(string workspaceRoot, string path)
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
            : Path.GetFullPath(Path.Combine(workspaceRoot, expanded));
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

        fullPath = WorkspaceBoundary.ResolveSymbolicLink(fullPath);
        if (_blacklist != null && _blacklist.IsBlacklisted(fullPath))
            return $"Error: Path '{originalPath}' is in the blacklist and cannot be accessed.";

        if (!RequiresOutsideWorkspaceApproval(fullPath, operation))
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

    /// <summary>
    /// Returns whether a non-blacklisted access needs an outside-workspace approval.
    /// This uses the same symlink-aware boundary and trusted-read rules as execution.
    /// </summary>
    public bool RequiresOutsideWorkspaceApproval(string fullPath, string operation)
    {
        if (IsWithinAny(fullPath, _workspaceRoots) || IsWithinAny(fullPath, _trustedPaths))
            return false;
        return operation is not ("read" or "list") || !IsWithinAny(fullPath, _trustedReadPaths);
    }

    private static bool IsWithinAny(string fullPath, IReadOnlyList<string> roots) =>
        roots.Any(root => WorkspaceBoundary.IsWithin(fullPath, root));
}
