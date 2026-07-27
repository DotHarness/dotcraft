namespace DotCraft.Protocol;

/// <summary>
/// Resolved thread working directory and runtime workspace boundaries.
/// </summary>
public sealed record ThreadWorkspaceContext(
    string Cwd,
    IReadOnlyList<string> RuntimeWorkspaceRoots);

/// <summary>
/// Applies and resolves sticky workspace inputs using DotCraft's partial-update contract.
/// </summary>
public static class ThreadWorkspaceResolver
{
    public static ThreadWorkspaceContext Resolve(SessionThread thread) =>
        Resolve(thread.WorkspacePath, thread.Configuration);

    public static ThreadWorkspaceContext Resolve(
        string workspacePath,
        ThreadConfiguration? configuration)
    {
        var ordinaryCwd = ResolveOrdinaryCwd(workspacePath, configuration);
        var roots = configuration?.RuntimeWorkspaceRoots is { } configuredRoots
            ? NormalizeRoots(configuredRoots)
            : [ordinaryCwd];

        var executionCwd = NormalizeOptional(configuration?.ExecutionWorkspaceOverride);
        if (executionCwd == null)
            return new ThreadWorkspaceContext(ordinaryCwd, roots);

        return new ThreadWorkspaceContext(
            executionCwd,
            RetargetRoots(roots, ordinaryCwd, executionCwd));
    }

    public static string ResolveOrdinaryCwd(SessionThread thread) =>
        ResolveOrdinaryCwd(thread.WorkspacePath, thread.Configuration);

    public static ThreadConfiguration Apply(
        string workspacePath,
        ThreadConfiguration? configuration,
        string? cwd,
        IReadOnlyList<string>? runtimeWorkspaceRoots)
    {
        var result = configuration ?? new ThreadConfiguration();
        var previousCwd = ResolveOrdinaryCwd(workspacePath, result);
        var nextCwd = NormalizeOptional(cwd);

        if (runtimeWorkspaceRoots != null)
        {
            result.RuntimeWorkspaceRoots = [.. NormalizeRoots(runtimeWorkspaceRoots)];
        }
        else if (nextCwd != null && result.RuntimeWorkspaceRoots != null)
        {
            result.RuntimeWorkspaceRoots =
                [.. RetargetRoots(result.RuntimeWorkspaceRoots, previousCwd, nextCwd)];
        }

        if (nextCwd != null)
            result.Cwd = nextCwd;

        return result;
    }

    private static string ResolveOrdinaryCwd(
        string workspacePath,
        ThreadConfiguration? configuration) =>
        NormalizeOptional(configuration?.Cwd)
        ?? NormalizeOptional(configuration?.WorkspaceOverride)
        ?? Path.GetFullPath(workspacePath);

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            if (!Path.IsPathFullyQualified(root))
                throw new ArgumentException($"Runtime workspace root must be absolute: '{root}'.");
            var normalized = Path.GetFullPath(root);
            if (seen.Add(normalized))
                result.Add(normalized);
        }

        return result;
    }

    private static IReadOnlyList<string> RetargetRoots(
        IEnumerable<string> roots,
        string previousCwd,
        string nextCwd)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            var normalized = Path.GetFullPath(root);
            var retargeted = PathComparer.Equals(normalized, previousCwd)
                ? nextCwd
                : normalized;
            if (seen.Add(retargeted))
                result.Add(retargeted);
        }

        return result;
    }

    private static string? NormalizeOptional(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
