using DotCraft.Automations.Abstractions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Maps between YAML status strings in task.md and <see cref="AutomationTaskStatus"/>.
/// </summary>
public static class LocalTaskStatusMapping
{
    public static AutomationTaskStatus FromYaml(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return AutomationTaskStatus.Pending;

        return status.Trim().ToLowerInvariant() switch
        {
            "pending" => AutomationTaskStatus.Pending,
            "running" => AutomationTaskStatus.Running,
            "completed" => AutomationTaskStatus.Completed,
            "failed" => AutomationTaskStatus.Failed,
            _ => throw new InvalidOperationException($"Unsupported automation task status '{status}'.")
        };
    }

    public static string ToYaml(AutomationTaskStatus status) => status switch
    {
        AutomationTaskStatus.Pending => "pending",
        AutomationTaskStatus.Running => "running",
        AutomationTaskStatus.Completed => "completed",
        AutomationTaskStatus.Failed => "failed",
        _ => "pending"
    };
}
