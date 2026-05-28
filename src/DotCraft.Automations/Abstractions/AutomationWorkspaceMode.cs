namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Where local automation tasks run: project root or an isolated directory under <c>.craft/tasks</c>.
/// </summary>
public enum AutomationWorkspaceMode
{
    /// <summary>Agent tools use the DotCraft workspace root (project).</summary>
    Project,

    /// <summary>Agent tools use <c>{taskDir}/workspace</c> only.</summary>
    Isolated
}
