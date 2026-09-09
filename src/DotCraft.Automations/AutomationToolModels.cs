using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace DotCraft.Automations;

/// <summary>Operations exposed by the model-visible automation tool.</summary>
public enum AutomationToolAction
{
    List,
    Read,
    Create,
    Update,
    Pause,
    Resume,
    Delete,
    Run,
    Report
}

/// <summary>Editable lifecycle values accepted from the model.</summary>
public enum AutomationToolStatus
{
    Active,
    Paused
}

/// <summary>Conversation strategy for an automation run.</summary>
public enum AutomationToolExecutionMode
{
    Thread,
    Independent
}

/// <summary>Workspace strategy for independent runs.</summary>
public enum AutomationToolWorkspaceMode
{
    Project,
    Worktree
}

/// <summary>Unattended approval policy for independent runs.</summary>
public enum AutomationToolApprovalPolicy
{
    WorkspaceScope,
    FullAuto
}

/// <summary>Delivery policy for run results.</summary>
public enum AutomationToolNotificationPolicy
{
    Important,
    All,
    Failures
}

/// <summary>Supported schedule shapes at the model boundary.</summary>
public enum AutomationToolScheduleKind
{
    At,
    Every,
    Daily,
    Weekdays,
    Weekly
}

/// <summary>Constrained schedule input for the model-visible tool.</summary>
public sealed record AutomationToolSchedule
{
    [Description("Schedule kind.")]
    public AutomationToolScheduleKind Kind { get; init; } = AutomationToolScheduleKind.At;

    [Description("Absolute UTC instant for at schedules.")]
    public DateTimeOffset? At { get; init; }

    [Range(1L, 315576000000L)]
    [Description("Interval in milliseconds for every schedules.")]
    public long? EveryMs { get; init; }

    [Range(0, 23)]
    [Description("Local hour for calendar schedules.")]
    public int? Hour { get; init; }

    [Range(0, 59)]
    [Description("Local minute for calendar schedules.")]
    public int? Minute { get; init; }

    [Description("IANA or Windows time zone for calendar schedules.")]
    public string? TimeZone { get; init; }

    [MinLength(1)]
    [MaxLength(7)]
    [Description("ISO weekdays for weekly schedules, Monday=1 through Sunday=7.")]
    public int[]? Days { get; init; }
}

/// <summary>Constrained editable definition accepted by the model-visible tool.</summary>
public sealed record AutomationToolInput
{
    [Required]
    [MinLength(1)]
    [MaxLength(200)]
    [Description("Short display name.")]
    public string Name { get; init; } = "";

    [Required]
    [MinLength(1)]
    [MaxLength(10000)]
    [Description("Instruction executed each run.")]
    public string Prompt { get; init; } = "";

    public AutomationToolStatus Status { get; init; } = AutomationToolStatus.Active;
    public AutomationToolExecutionMode ExecutionMode { get; init; } = AutomationToolExecutionMode.Independent;
    public string? TargetThreadId { get; init; }
    public AutomationToolWorkspaceMode? WorkspaceMode { get; init; }
    public string? AgentProfileId { get; init; }
    public AutomationToolApprovalPolicy ApprovalPolicy { get; init; } = AutomationToolApprovalPolicy.WorkspaceScope;

    [Required]
    public AutomationToolSchedule Schedule { get; init; } = new();

    public AutomationToolNotificationPolicy? NotificationPolicy { get; init; }
}
