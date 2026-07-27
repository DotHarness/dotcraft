namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Where local automation tasks run: project root or a managed Git worktree.
/// </summary>
public enum AutomationWorkspaceMode
{
    /// <summary>Agent tools use the DotCraft workspace root (project).</summary>
    Project,

    /// <summary>Agent tools use a managed worktree under <c>.craft/worktrees</c>.</summary>
    Worktree
}

/// <summary>
/// Canonical wire and file names for automation workspace modes.
/// </summary>
public static class AutomationWorkspaceModeNames
{
    public const string Project = "project";
    public const string Worktree = "worktree";

    /// <summary>
    /// Converts a persisted or wire value to the canonical workspace mode name.
    /// </summary>
    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        if (string.Equals(trimmed, Project, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Project;
            return true;
        }

        if (string.Equals(trimmed, Worktree, StringComparison.OrdinalIgnoreCase))
        {
            normalized = Worktree;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Converts an optional workspace mode value to a canonical name, using
    /// <paramref name="defaultValue"/> when the input is absent.
    /// </summary>
    public static string NormalizeOrDefault(string? value, string defaultValue = Project)
    {
        if (!TryNormalize(value, out var normalized))
            throw new ArgumentException("Workspace mode must be 'project' or 'worktree'.", nameof(value));

        return string.IsNullOrWhiteSpace(normalized) ? defaultValue : normalized!;
    }

    public static AutomationWorkspaceMode ToMode(string? value) =>
        NormalizeOrDefault(value) == Worktree
            ? AutomationWorkspaceMode.Worktree
            : AutomationWorkspaceMode.Project;

    public static string ToCanonicalString(AutomationWorkspaceMode mode) =>
        mode == AutomationWorkspaceMode.Worktree ? Worktree : Project;
}
