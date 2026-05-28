namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Workflow steps and round limit for a local automation task.
/// </summary>
public sealed class AutomationWorkflowDefinition
{
    /// <summary>Ordered prompts executed as separate turns per round.</summary>
    public required IReadOnlyList<WorkflowStep> Steps { get; init; }

    /// <summary>Maximum full passes over <see cref="Steps"/> before stopping.</summary>
    public int MaxRounds { get; init; } = 10;

    /// <summary>Whether the agent runs in the project root or an isolated task workspace folder.</summary>
    public AutomationWorkspaceMode WorkspaceMode { get; init; } = AutomationWorkspaceMode.Project;
}

/// <summary>
/// A single workflow step: one user turn with the given prompt text.
/// </summary>
public sealed class WorkflowStep
{
    public required string Prompt { get; init; }
}
