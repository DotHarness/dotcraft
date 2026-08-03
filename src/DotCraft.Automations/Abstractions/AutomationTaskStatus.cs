using AutomationTask = DotCraft.Automations.Abstractions.AutomationTask;
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
