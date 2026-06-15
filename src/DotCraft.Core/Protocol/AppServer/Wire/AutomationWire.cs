using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;


// ───── automation/task/* DTOs ─────

public sealed class AutomationTaskWire
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentSummary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Local task tool boundary: <c>workspaceScope</c> (default, reject outside thread workspace) or <c>fullAuto</c> (legacy <c>autoApprove</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Optional Agent Profile bound to the task; governs the agent's capabilities
    /// (tools, MCP, skills, model, instructions). Null when the task runs with the
    /// default automation agent. Only the id is persisted; the profile is resolved
    /// at each dispatch.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; set; }

    /// <summary>
    /// Canonical declared workspace mode: <c>project</c> or <c>worktree</c>.
    /// </summary>
    public string WorkspaceMode { get; set; } = "project";

    /// <summary>
    /// Managed worktree identity once provisioned. Null before first dispatch, for project mode,
    /// for bound tasks, and when a worktree task falls back to the legacy sandbox.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationTaskWorktreeWire? Worktree { get; set; }

    /// <summary>
    /// Optional recurring schedule. When omitted, the task is one-shot (runs once from <c>pending</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? Schedule { get; set; }

    /// <summary>
    /// Optional binding to a pre-existing thread; the orchestrator submits workflow turns directly into that thread.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }

    /// <summary>
    /// Scheduled next-run time (UTC). Null when the task has no <see cref="Schedule"/> or is ready to dispatch immediately.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? NextRunAt { get; set; }
}

public sealed class AutomationTaskWorktreeWire
{
    public string BranchName { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// Wire-level projection of <c>CronSchedule</c> reused by automation tasks for serialization.
/// Kind: <c>once</c> | <c>every</c> | <c>at</c> | <c>daily</c> (weekly reserved).
/// </summary>
public sealed class AutomationScheduleWire
{
    public string Kind { get; set; } = "once";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EveryMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InitialDelayMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyHour { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DailyMinute { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expr { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Tz { get; set; }
}

public sealed class AutomationThreadBindingWire
{
    public string ThreadId { get; set; } = string.Empty;

    /// <summary><c>run-in-thread</c> (default).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }
}

public sealed class AutomationTaskListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }
}

public sealed class AutomationTaskListResult
{
    public List<AutomationTaskWire> Tasks { get; set; } = [];
}

public sealed class AutomationTaskReadParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskCreateParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkflowTemplate { get; set; }

    /// <summary>
    /// <c>workspaceScope</c> (default) or <c>fullAuto</c>. Legacy <c>autoApprove</c> / <c>default</c> are accepted when reading tasks.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalPolicy { get; set; }

    /// <summary>
    /// Explicit task workspace mode. Written into generated <c>workflow.md</c> when
    /// <see cref="WorkflowTemplate"/> is omitted, and overrides the template workflow front matter when supplied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspaceMode { get; set; }

    /// <summary>Optional recurring schedule. Null/absent = one-shot task.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? Schedule { get; set; }

    /// <summary>Optional existing thread to bind this task to.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }

    /// <summary>
    /// Optional Agent Profile id binding the task to a profile that governs the agent's capabilities.
    /// Persisted as <c>agent_profile_id</c> in <c>task.md</c>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; set; }

    /// <summary>
    /// Optional template id the dialog selected; persisted in front-matter for telemetry / re-apply.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateId { get; set; }
}

public sealed class AutomationTaskCreateResult
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskDirectory { get; set; } = string.Empty;
}

public sealed class AutomationTaskDeleteParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskDiscardWorktreeParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskRunParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
}

public sealed class AutomationTaskRunResult
{
    public AutomationTaskWire Task { get; set; } = new();
}

public sealed class AutomationTaskDiscardWorktreeResult
{
    public AutomationTaskWire Task { get; set; } = new();
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTaskUpdateBinding"/>.
/// When <see cref="ThreadBinding"/> is null the task is unbound (uses its own automation thread).
/// </summary>
public sealed class AutomationTaskUpdateBindingParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationThreadBindingWire? ThreadBinding { get; set; }
}

public sealed class AutomationTaskUpdateBindingResult
{
    public AutomationTaskWire Task { get; set; } = new();
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTemplateList"/>. Currently takes no fields;
/// reserved for future locale / capability filters.
/// </summary>
public sealed class AutomationTemplateListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Locale { get; set; }
}

public sealed class AutomationTemplateWire
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>Complete <c>workflow.md</c> contents (includes front matter).</summary>
    public string WorkflowMarkdown { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? DefaultSchedule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultWorkspaceMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultApprovalPolicy { get; set; }

    /// <summary>
    /// Optional Agent Profile id that pre-fills the task Agent picker when this template is
    /// applied. A default, not itself executable.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultAgentProfileId { get; set; }

    /// <summary>
    /// Suggests to the UI that this template benefits from being bound to an existing thread
    /// (e.g. feishu-reply watchers). The dialog may surface the thread picker up-front.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NeedsThreadBinding { get; set; }

    /// <summary>Seed text for the new task description / prompt body.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultDescription { get; set; }

    /// <summary>Seed title for the New Task dialog.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultTitle { get; set; }

    /// <summary>
    /// True when this template is user-authored (editable / deletable). Absent or false for
    /// built-in templates shipped by the server. Surfaced to the desktop gallery so it can
    /// render edit / delete controls on the right cards only.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsUser { get; set; }

    /// <summary>ISO-8601 UTC, only populated for user templates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>ISO-8601 UTC, only populated for user templates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AutomationTemplateListResult
{
    public List<AutomationTemplateWire> Templates { get; set; } = [];
}

/// <summary>
/// Params for <see cref="AppServerMethods.AutomationTemplateSave"/>.
/// Upsert semantics: when <see cref="Id"/> is supplied and matches an existing user template,
/// the save overwrites that template; otherwise the server assigns a fresh id.
/// </summary>
public sealed class AutomationTemplateSaveParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>Complete <c>workflow.md</c> contents (with front matter).</summary>
    public string WorkflowMarkdown { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AutomationScheduleWire? DefaultSchedule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultWorkspaceMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultApprovalPolicy { get; set; }

    /// <summary>Optional Agent Profile id pre-filled into the task Agent picker when applied.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultAgentProfileId { get; set; }

    public bool NeedsThreadBinding { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultTitle { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefaultDescription { get; set; }
}

/// <summary>Result of <see cref="AppServerMethods.AutomationTemplateSave"/>.</summary>
public sealed class AutomationTemplateSaveResult
{
    public AutomationTemplateWire Template { get; set; } = new();
}

/// <summary>Params for <see cref="AppServerMethods.AutomationTemplateDelete"/>.</summary>
public sealed class AutomationTemplateDeleteParams
{
    public string Id { get; set; } = string.Empty;
}

/// <summary>Result of <see cref="AppServerMethods.AutomationTemplateDelete"/>.</summary>
public sealed class AutomationTemplateDeleteResult
{
    public bool Ok { get; set; } = true;
}
