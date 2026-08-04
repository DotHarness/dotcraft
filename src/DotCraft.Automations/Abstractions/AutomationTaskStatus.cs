namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Lifecycle state for an <see cref="AutomationTask"/>.
/// </summary>
public enum AutomationTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
