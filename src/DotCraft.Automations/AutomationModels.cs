using DotCraft.Sessions;
using System.ComponentModel;

namespace DotCraft.Automations;

/// <summary>Editable automation settings shared by tools and protocol clients.</summary>
public record AutomationInput
{
    [Description("Short display name, at most 200 characters.")]
    public string Name { get; init; } = "";
    [Description("Instruction executed each run, at most 10000 characters.")]
    public string Prompt { get; init; } = "";
    [Description("Editable lifecycle state: active or paused. Completed is server-owned for finished one-shot automations.")]
    public string Status { get; init; } = "active";
    [Description("thread for follow-ups in an existing conversation; independent for a fresh conversation each run.")]
    public string ExecutionMode { get; init; } = "independent";
    [Description("Conversation identifier for thread mode; defaults to the current conversation when created by the tool.")]
    public string? TargetThreadId { get; init; }
    [Description("project or worktree; omitted defaults to worktree for Git workspaces, project otherwise.")]
    public string? WorkspaceMode { get; init; }
    [Description("Optional existing agent profile identifier. Omit when no profile is selected.")]
    public string? AgentProfileId { get; init; }
    [Description("Approval policy: workspaceScope (default) or fullAuto.")]
    public string ApprovalPolicy { get; init; } = "workspaceScope";
    public AutomationSchedule Schedule { get; init; } = new();
    [Description("Notification policy nested in this automation definition: important, all, or failures. Defaults to important for thread mode and all for independent mode.")]
    public string? NotificationPolicy { get; init; }
}

/// <summary>A versioned durable scheduled instruction.</summary>
public sealed record AutomationDefinition : AutomationInput
{
    public string Id { get; init; } = "";
    public int Version { get; init; }
    public AutomationOrigin? Origin { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? NextRunAt { get; init; }
}

/// <summary>Host-captured delivery identity, independent of execution mode.</summary>
public sealed record AutomationOrigin
{
    public string Channel { get; init; } = "";
    public string UserId { get; init; } = "";
    public string? GroupId { get; init; }
    public string? DeliveryTarget { get; init; }
}

/// <summary>One execution attempt and its independently tracked delivery.</summary>
public sealed record AutomationRun
{
    public string Id { get; init; } = "";
    public string AutomationId { get; init; } = "";
    public int DefinitionVersion { get; init; }
    public string Status { get; init; } = "queued";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? ScheduledAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? ThreadId { get; init; }
    public string? TurnId { get; init; }
    public string? Summary { get; init; }
    public string? Error { get; init; }
    public ThreadWorktreeInfo? Worktree { get; init; }
    public string DeliveryStatus { get; init; } = "pending";
    public string? DeliveryError { get; init; }
}

/// <summary>A conversational starting point, never an executable workflow.</summary>
public sealed record AutomationPreset(string Id, string Name, string Prompt, AutomationSchedule? Schedule = null);
