using DotCraft.Automations.Abstractions;
using DotCraft.Protocol;
using DotCraft.Sessions;
using AutomationTask = DotCraft.Automations.Abstractions.AutomationTask;

namespace DotCraft.Automations.Local;

/// <summary>
/// Automation task backed by a <c>task.md</c> file under the local tasks root.
/// </summary>
public sealed class LocalAutomationTask : AutomationTask
{
    /// <summary>Absolute path to the task directory (contains task.md, workflow.md, workspace/).</summary>
    public required string TaskDirectory { get; init; }

    /// <summary>Absolute path to task.md.</summary>
    public string TaskFilePath => Path.Combine(TaskDirectory, "task.md");

    /// <summary>Absolute path to workflow.md.</summary>
    public string WorkflowFilePath => Path.Combine(TaskDirectory, "workflow.md");

    /// <summary>
    /// Absolute path to the provisioned agent workspace directory (set by the orchestrator before workflow load).
    /// </summary>
    public string? AgentWorkspacePath { get; set; }

    /// <summary>
    /// Canonical workspace mode read from <c>workflow.md</c>. This is runtime metadata;
    /// <c>workflow.md</c> remains the source of truth.
    /// </summary>
    public AutomationWorkspaceMode WorkspaceMode { get; set; } = AutomationWorkspaceMode.Project;

    /// <summary>
    /// Worktree metadata from the task thread, populated after provisioning.
    /// </summary>
    public ThreadWorktreeInfo? Worktree { get; set; }

    /// <summary>
    /// Serialized as <c>approval_policy</c> in task.md: <c>workspaceScope</c> (default, reject tools outside agent workspace)
    /// or <c>fullAuto</c>.
    /// </summary>
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Serialized as <c>agent_profile_id</c> in task.md. Optional Agent Profile bound to the task that
    /// governs the agent's capabilities (tools, MCP, skills, model, instructions). Null when the task
    /// runs with the default automation agent. Only the id is persisted; the orchestrator resolves the
    /// profile into the task thread configuration at each dispatch and fails the run if it no longer resolves.
    /// </summary>
    public string? AgentProfileId { get; set; }
}
