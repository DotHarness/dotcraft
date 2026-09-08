using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>Unified automation contract for AutomationSchedule.</summary>
[ContractModule("automations")]
public sealed class AutomationSchedule
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "daily";

    [JsonPropertyName("at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? At { get; init; } = null;

    [JsonPropertyName("everyMs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSafeInteger]
    public long? EveryMs { get; init; } = null;

    [JsonPropertyName("hour")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Hour { get; init; } = null;

    [JsonPropertyName("minute")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Minute { get; init; } = null;

    [JsonPropertyName("timeZone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimeZone { get; init; } = null;

    [JsonPropertyName("days")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<int>? Days { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationOrigin.</summary>
[ContractModule("automations")]
public sealed class AutomationOrigin
{
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "";

    [JsonPropertyName("userId")]
    public string UserId { get; init; } = "";

    [JsonPropertyName("groupId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; init; } = null;

    [JsonPropertyName("deliveryTarget")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryTarget { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationInput.</summary>
[ContractModule("automations")]
public sealed class AutomationInput
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; init; } = "independent";

    [JsonPropertyName("targetThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetThreadId { get; init; } = null;

    [JsonPropertyName("workspaceMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceMode { get; init; } = null;

    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; init; } = null;

    [JsonPropertyName("approvalPolicy")]
    public string ApprovalPolicy { get; init; } = "workspaceScope";

    [JsonPropertyName("schedule")]
    public AutomationSchedule Schedule { get; init; } = new();

    [JsonPropertyName("notificationPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NotificationPolicy { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationDefinition.</summary>
[ContractModule("automations")]
public sealed class AutomationDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";

    [JsonPropertyName("executionMode")]
    public string ExecutionMode { get; init; } = "independent";

    [JsonPropertyName("targetThreadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetThreadId { get; init; } = null;

    [JsonPropertyName("workspaceMode")]
    public string WorkspaceMode { get; init; } = "project";

    [JsonPropertyName("agentProfileId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; init; } = null;

    [JsonPropertyName("approvalPolicy")]
    public string ApprovalPolicy { get; init; } = "workspaceScope";

    [JsonPropertyName("schedule")]
    public AutomationSchedule Schedule { get; init; } = new();

    [JsonPropertyName("notificationPolicy")]
    public string NotificationPolicy { get; init; } = "all";

    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationOrigin? Origin { get; init; } = null;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = default;

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; init; } = default;

    [JsonPropertyName("nextRunAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationRun.</summary>
[ContractModule("automations")]
public sealed class AutomationRun
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("automationId")]
    public string AutomationId { get; init; } = "";

    [JsonPropertyName("definitionVersion")]
    public int DefinitionVersion { get; init; } = 1;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "queued";

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = default;

    [JsonPropertyName("scheduledAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ScheduledAt { get; init; }

    [JsonPropertyName("startedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? StartedAt { get; init; } = null;

    [JsonPropertyName("completedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; init; } = null;

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; init; } = null;

    [JsonPropertyName("turnId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TurnId { get; init; } = null;

    [JsonPropertyName("summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; init; } = null;

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; } = null;

    [JsonPropertyName("worktree")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadWorktreeInfo? Worktree { get; init; } = null;

    [JsonPropertyName("deliveryStatus")]
    public string DeliveryStatus { get; init; } = "pending";

    [JsonPropertyName("deliveryError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveryError { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationPreset.</summary>
[ContractModule("automations")]
public sealed class AutomationPreset
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = "";

    [JsonPropertyName("schedule")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationSchedule? Schedule { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationIdParams.</summary>
[ContractModule("automations")]
public sealed class AutomationIdParams
{
    [JsonPropertyName("automationId")]
    public string AutomationId { get; init; } = "";

}

/// <summary>Unified automation contract for AutomationCreateParams.</summary>
[ContractModule("automations")]
public sealed class AutomationCreateParams
{
    [JsonPropertyName("automation")]
    public AutomationInput Automation { get; init; } = new();

}

/// <summary>Unified automation contract for AutomationUpdateParams.</summary>
[ContractModule("automations")]
public sealed class AutomationUpdateParams
{
    [JsonPropertyName("automationId")]
    public string AutomationId { get; init; } = "";

    [JsonPropertyName("expectedVersion")]
    public int ExpectedVersion { get; init; } = 0;

    [JsonPropertyName("automation")]
    public AutomationInput Automation { get; init; } = new();

}

/// <summary>Unified automation contract for AutomationListResult.</summary>
[ContractModule("automations")]
public sealed class AutomationListResult
{
    [JsonPropertyName("automations")]
    public List<AutomationDefinition> Automations { get; init; } = [];

}

/// <summary>Unified automation contract for AutomationReadResult.</summary>
[ContractModule("automations")]
public sealed class AutomationReadResult
{
    [JsonPropertyName("automation")]
    public AutomationDefinition Automation { get; init; } = new();

}

/// <summary>Unified automation contract for AutomationDeleteResult.</summary>
[ContractModule("automations")]
public sealed class AutomationDeleteResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = false;

}

/// <summary>Unified automation contract for AutomationRunResult.</summary>
[ContractModule("automations")]
public sealed class AutomationRunResult
{
    [JsonPropertyName("run")]
    public AutomationRun Run { get; init; } = new();

}

/// <summary>Unified automation contract for AutomationRunsResult.</summary>
[ContractModule("automations")]
public sealed class AutomationRunsResult
{
    [JsonPropertyName("runs")]
    public List<AutomationRun> Runs { get; init; } = [];

}

/// <summary>Unified automation contract for AutomationPresetsParams.</summary>
[ContractModule("automations")]
public sealed class AutomationPresetsParams
{
    [JsonPropertyName("locale")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; init; } = null;

}

/// <summary>Unified automation contract for AutomationPresetsResult.</summary>
[ContractModule("automations")]
public sealed class AutomationPresetsResult
{
    [JsonPropertyName("presets")]
    public List<AutomationPreset> Presets { get; init; } = [];

}

/// <summary>Unified automation contract for AutomationUpdatedNotification.</summary>
[ContractModule("automations")]
public sealed class AutomationUpdatedNotification
{
    [JsonPropertyName("automationId")]
    public string AutomationId { get; init; } = "";

    [JsonPropertyName("automation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationDefinition? Automation { get; init; } = null;

    [JsonPropertyName("removed")]
    public bool Removed { get; init; } = false;

}

/// <summary>Unified automation contract for AutomationRunUpdatedNotification.</summary>
[ContractModule("automations")]
public sealed class AutomationRunUpdatedNotification
{
    [JsonPropertyName("run")]
    public AutomationRun Run { get; init; } = new();

}

