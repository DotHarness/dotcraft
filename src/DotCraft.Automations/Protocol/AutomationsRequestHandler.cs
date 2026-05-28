using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Templates;
using DotCraft.Cron;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Protocol;

/// <summary>
/// Handles <c>automation/*</c> Wire Protocol requests.
/// Registered as a nullable dependency in <see cref="AppServerRequestHandler"/>.
/// </summary>
public sealed partial class AutomationsRequestHandler(
    AutomationOrchestrator orchestrator,
    LocalTaskFileStore fileStore,
    UserTemplateFileStore userTemplateStore) : IAutomationsRequestHandler
{
    // Automation-specific error codes are now in AppServerErrors (-32051 to -32054)

    public async Task<object?> HandleTaskListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var tasks = await orchestrator.GetAllTasksAsync(ct);

        return new AutomationTaskListResult
        {
            Tasks = tasks.Select(ToWire).ToList()
        };
    }

    public async Task<object?> HandleTaskReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskReadParams>(msg);
        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, p.TaskId, StringComparison.Ordinal));

        if (task == null)
            throw AppServerErrors.TaskNotFound(p.TaskId);

        return ToWireDetailed(task);
    }

    public Task<object?> HandleTaskCreateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskCreateParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Title))
            throw AppServerErrors.InvalidParams("'title' is required.");

        // Validate title length
        if (p.Title.Length > 200)
            throw AppServerErrors.InvalidParams("'title' must be 200 characters or less.");

        // Validate description length
        if (p.Description != null && p.Description.Length > 10000)
            throw AppServerErrors.InvalidParams("'description' must be 10000 characters or less.");

        var taskId = GenerateTaskId(p.Title, p.TemplateId);
        
        // Security: Validate the generated task ID to prevent path traversal
        if (!IsValidTaskId(taskId))
            throw AppServerErrors.InvalidParams("Generated task ID contains invalid characters.");

        var taskDir = Path.Combine(fileStore.TasksRoot, taskId);
        
        // Security: Ensure the task directory is within TasksRoot (path traversal protection)
        var fullTaskDir = Path.GetFullPath(taskDir);
        var fullRoot = Path.GetFullPath(fileStore.TasksRoot);
        if (!fullTaskDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw AppServerErrors.InvalidParams("Invalid task directory path.");

        // Check for task ID collision (rare but possible with rapid creation)
        if (Directory.Exists(fullTaskDir))
            throw AppServerErrors.TaskAlreadyExists(taskId);

        Directory.CreateDirectory(taskDir);

        var now = DateTimeOffset.UtcNow.ToString("o");
        var description = p.Description ?? "";
        var approvalPolicy = string.IsNullOrWhiteSpace(p.ApprovalPolicy)
            ? "workspaceScope"
            : p.ApprovalPolicy.Trim();

        var fm = new StringBuilder();
        fm.AppendLine("---");
        fm.AppendLine($"id: \"{taskId}\"");
        fm.AppendLine($"title: \"{EscapeYamlString(p.Title)}\"");
        fm.AppendLine("status: pending");
        fm.AppendLine($"created_at: \"{now}\"");
        fm.AppendLine($"updated_at: \"{now}\"");
        fm.AppendLine("thread_id: null");
        fm.AppendLine("agent_summary: null");
        fm.AppendLine($"approval_policy: \"{EscapeYamlString(approvalPolicy)}\"");

        if (!string.IsNullOrWhiteSpace(p.TemplateId))
            fm.AppendLine($"template_id: \"{EscapeYamlString(p.TemplateId)}\"");

        AppendScheduleYaml(fm, p.Schedule);
        AppendThreadBindingYaml(fm, p.ThreadBinding);

        fm.AppendLine("---");
        fm.AppendLine();
        fm.Append(description);

        File.WriteAllText(Path.Combine(taskDir, "task.md"), fm.ToString());

        var workflowContent = string.IsNullOrWhiteSpace(p.WorkflowTemplate)
            ? BuildDefaultWorkflowContent(NormalizeWorkspaceMode(p.WorkspaceMode))
            : p.WorkflowTemplate;
        File.WriteAllText(Path.Combine(taskDir, "workflow.md"), workflowContent);

        return Task.FromResult<object?>(new AutomationTaskCreateResult
        {
            TaskId = taskId,
            TaskDirectory = taskDir
        });
    }

    public async Task<object?> HandleTaskUpdateBindingAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskUpdateBindingParams>(msg);
        if (string.IsNullOrWhiteSpace(p.TaskId))
            throw AppServerErrors.InvalidParams("'taskId' is required.");

        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, p.TaskId, StringComparison.Ordinal))
            as LocalAutomationTask;

        if (task == null)
            throw AppServerErrors.TaskNotFound(p.TaskId);

        // Safety: don't rebind a task that is currently running; the frontend should confirm first.
        if (task.Status == AutomationTaskStatus.Running)
            throw AppServerErrors.TaskInvalidStatus(
                "Cannot change binding while the task is running. Cancel the run first.");

        task.ThreadBinding = p.ThreadBinding == null || string.IsNullOrWhiteSpace(p.ThreadBinding.ThreadId)
            ? null
            : new AutomationThreadBinding
            {
                ThreadId = p.ThreadBinding.ThreadId,
                Mode = string.IsNullOrWhiteSpace(p.ThreadBinding.Mode) ? "run-in-thread" : p.ThreadBinding.Mode!
            };

        await fileStore.SaveAsync(task, ct);

        return new AutomationTaskUpdateBindingResult { Task = ToWireDetailed(task) };
    }

    public async Task<object?> HandleTaskRunAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskRunParams>(msg);
        if (string.IsNullOrWhiteSpace(p.TaskId))
            throw AppServerErrors.InvalidParams("'taskId' is required.");

        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, p.TaskId, StringComparison.Ordinal))
            as LocalAutomationTask;

        if (task == null)
            throw AppServerErrors.TaskNotFound(p.TaskId);

        if (task.Status == AutomationTaskStatus.Running)
            throw AppServerErrors.TaskInvalidStatus(
                "Cannot run a task that is already running.");

        task.Status = AutomationTaskStatus.Pending;
        task.NextRunAt = task.Schedule == null
            ? null
            : DateTimeOffset.UtcNow.AddMilliseconds(-1);
        await fileStore.SaveAsync(task, ct);

        _ = orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

        return new AutomationTaskRunResult { Task = ToWireDetailed(task) };
    }

    public async Task<object?> HandleTemplateListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTemplateListParams>(msg);

        var templates = new List<AutomationTemplateWire>();
        // Built-ins first so the UI can keep its existing ordering; user templates follow with IsUser=true.
        foreach (var t in LocalTaskTemplates.ForLocale(p.Locale))
            templates.Add(ToWire(t));

        var user = await userTemplateStore.LoadAllAsync(ct);
        foreach (var t in user)
            templates.Add(ToWire(t));

        return new AutomationTemplateListResult { Templates = templates };
    }

    public async Task<object?> HandleTemplateSaveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTemplateSaveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Title))
            throw AppServerErrors.InvalidParams("'title' is required.");
        if (p.Title.Length > 200)
            throw AppServerErrors.InvalidParams("'title' must be 200 characters or less.");
        if (string.IsNullOrWhiteSpace(p.WorkflowMarkdown))
            throw AppServerErrors.InvalidParams("'workflowMarkdown' is required.");

        var id = string.IsNullOrWhiteSpace(p.Id) ? GenerateUserTemplateId() : p.Id!.Trim();
        if (!UserTemplateFileStore.IsValidId(id))
            throw AppServerErrors.InvalidParams(
                "'id' must match ^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$.");
        if (LocalTaskTemplates.FindById(id) != null)
            throw AppServerErrors.InvalidParams(
                $"Template id '{id}' is reserved by a built-in template.");

        LocalTaskTemplate saved;
        try
        {
            saved = await userTemplateStore.SaveAsync(
                id: id,
                title: p.Title,
                description: p.Description,
                icon: p.Icon,
                category: p.Category,
                workflowMarkdown: p.WorkflowMarkdown,
                defaultSchedule: FromWire(p.DefaultSchedule),
                defaultWorkspaceMode: p.DefaultWorkspaceMode,
                defaultApprovalPolicy: p.DefaultApprovalPolicy,
                needsThreadBinding: p.NeedsThreadBinding,
                defaultTitle: p.DefaultTitle,
                defaultDescription: p.DefaultDescription,
                ct: ct);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        return new AutomationTemplateSaveResult { Template = ToWire(saved) };
    }

    public async Task<object?> HandleTemplateDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTemplateDeleteParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (LocalTaskTemplates.FindById(p.Id) != null)
            throw AppServerErrors.InvalidParams(
                $"Template id '{p.Id}' is a built-in template and cannot be deleted.");
        if (!UserTemplateFileStore.IsValidId(p.Id))
            throw AppServerErrors.InvalidParams("'id' has an invalid shape.");

        try
        {
            await userTemplateStore.DeleteAsync(p.Id, ct);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        return new AutomationTemplateDeleteResult { Ok = true };
    }

    private static AutomationTemplateWire ToWire(LocalTaskTemplate t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description,
        Icon = string.IsNullOrWhiteSpace(t.Icon) ? null : t.Icon,
        Category = string.IsNullOrWhiteSpace(t.Category) ? null : t.Category,
        WorkflowMarkdown = t.WorkflowMarkdown,
        DefaultSchedule = ToWire(t.DefaultSchedule),
        DefaultWorkspaceMode = t.DefaultWorkspaceMode,
        DefaultApprovalPolicy = t.DefaultApprovalPolicy,
        NeedsThreadBinding = t.NeedsThreadBinding,
        DefaultTitle = t.DefaultTitle,
        DefaultDescription = t.DefaultDescription,
        IsUser = t.IsUser,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };

    private static CronSchedule? FromWire(AutomationScheduleWire? wire)
    {
        if (wire == null || string.IsNullOrWhiteSpace(wire.Kind))
            return null;
        var kind = wire.Kind.Trim().ToLowerInvariant();
        if (kind == "once")
            return null;
        return new CronSchedule
        {
            Kind = kind,
            AtMs = wire.AtMs,
            EveryMs = wire.EveryMs,
            InitialDelayMs = wire.InitialDelayMs,
            DailyHour = wire.DailyHour,
            DailyMinute = wire.DailyMinute,
            Expr = wire.Expr,
            Tz = wire.Tz
        };
    }

    private static string GenerateUserTemplateId() =>
        "user-" + Guid.NewGuid().ToString("N")[..10];

    private static void AppendScheduleYaml(StringBuilder sb, AutomationScheduleWire? schedule)
    {
        if (schedule == null || string.IsNullOrWhiteSpace(schedule.Kind))
            return;
        var kind = schedule.Kind.Trim().ToLowerInvariant();
        if (kind == "once")
            return;
        sb.AppendLine("schedule:");
        sb.AppendLine($"  kind: \"{EscapeYamlString(kind)}\"");
        if (schedule.AtMs.HasValue) sb.AppendLine($"  at_ms: {schedule.AtMs.Value}");
        if (schedule.EveryMs.HasValue) sb.AppendLine($"  every_ms: {schedule.EveryMs.Value}");
        if (schedule.InitialDelayMs.HasValue) sb.AppendLine($"  initial_delay_ms: {schedule.InitialDelayMs.Value}");
        if (schedule.DailyHour.HasValue) sb.AppendLine($"  daily_hour: {schedule.DailyHour.Value}");
        if (schedule.DailyMinute.HasValue) sb.AppendLine($"  daily_minute: {schedule.DailyMinute.Value}");
        if (!string.IsNullOrWhiteSpace(schedule.Expr)) sb.AppendLine($"  expr: \"{EscapeYamlString(schedule.Expr!)}\"");
        if (!string.IsNullOrWhiteSpace(schedule.Tz)) sb.AppendLine($"  tz: \"{EscapeYamlString(schedule.Tz!)}\"");
    }

    private static void AppendThreadBindingYaml(StringBuilder sb, AutomationThreadBindingWire? binding)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.ThreadId))
            return;
        sb.AppendLine("thread_binding:");
        sb.AppendLine($"  thread_id: \"{EscapeYamlString(binding.ThreadId)}\"");
        var mode = string.IsNullOrWhiteSpace(binding.Mode) ? "run-in-thread" : binding.Mode!.Trim();
        sb.AppendLine($"  mode: \"{EscapeYamlString(mode)}\"");
    }

    public async Task<object?> HandleTaskDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AutomationTaskDeleteParams>(msg);
        try
        {
            await orchestrator.DeleteTaskAsync(p.TaskId, ct);
        }
        catch (KeyNotFoundException)
        {
            throw AppServerErrors.TaskNotFound(p.TaskId);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }

        return new { ok = true };
    }

    #region Helpers

    private static AutomationTaskWire ToWire(AutomationTask task)
    {
        var w = new AutomationTaskWire
        {
            Id = task.Id,
            Title = task.Title,
            Status = StatusToWire(task.Status),
            ThreadId = task.ThreadId,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            Schedule = ToWire(task.Schedule),
            ThreadBinding = ToWire(task.ThreadBinding),
            NextRunAt = task.NextRunAt
        };
        if (task is LocalAutomationTask local)
            w.ApprovalPolicy = local.ApprovalPolicy;
        return w;
    }

    private static AutomationTaskWire ToWireDetailed(AutomationTask task)
    {
        var w = new AutomationTaskWire
        {
            Id = task.Id,
            Title = task.Title,
            Status = StatusToWire(task.Status),
            ThreadId = task.ThreadId,
            Description = task.Description,
            AgentSummary = task.AgentSummary,
            CreatedAt = task.CreatedAt,
            UpdatedAt = task.UpdatedAt,
            Schedule = ToWire(task.Schedule),
            ThreadBinding = ToWire(task.ThreadBinding),
            NextRunAt = task.NextRunAt
        };
        if (task is LocalAutomationTask local)
            w.ApprovalPolicy = local.ApprovalPolicy;
        return w;
    }

    private static AutomationScheduleWire? ToWire(CronSchedule? schedule)
    {
        if (schedule == null)
            return null;
        return new AutomationScheduleWire
        {
            Kind = schedule.Kind,
            AtMs = schedule.AtMs,
            EveryMs = schedule.EveryMs,
            InitialDelayMs = schedule.InitialDelayMs,
            DailyHour = schedule.DailyHour,
            DailyMinute = schedule.DailyMinute,
            Expr = schedule.Expr,
            Tz = schedule.Tz
        };
    }

    private static AutomationThreadBindingWire? ToWire(AutomationThreadBinding? binding)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.ThreadId))
            return null;
        return new AutomationThreadBindingWire
        {
            ThreadId = binding.ThreadId,
            Mode = binding.Mode
        };
    }

    /// <summary>
    /// Converts <see cref="AutomationTaskWire"/> from an <see cref="AutomationTask"/>
    /// for use in <c>automation/task/updated</c> notifications.
    /// </summary>
    public static AutomationTaskWire ToNotificationWire(AutomationTask task) => ToWire(task);

    private static string StatusToWire(AutomationTaskStatus status) => status switch
    {
        AutomationTaskStatus.Pending => "pending",
        AutomationTaskStatus.Running => "running",
        AutomationTaskStatus.Completed => "completed",
        AutomationTaskStatus.Failed => "failed",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string GenerateTaskId(string title, string? templateId = null)
    {
        var slug = SlugRegex().Replace(title.ToLowerInvariant(), "-").Trim('-');
        if (string.IsNullOrEmpty(slug) && !string.IsNullOrWhiteSpace(templateId))
            slug = SlugRegex().Replace(templateId.ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (string.IsNullOrEmpty(slug)) slug = "task";
        return $"{slug}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
    }

    private static string EscapeYamlString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    /// <summary>
    /// <paramref name="workspaceYamlValue"/> is <c>project</c> or <c>isolated</c> (validated by <see cref="NormalizeWorkspaceMode"/>).
    /// Liquid body uses <c>{{ }}</c>; keep it in a non-interpolated raw string to avoid C# brace escaping.
    /// </summary>
    private static string BuildDefaultWorkflowContent(string workspaceYamlValue)
    {
        const string Body = """

            You are running a local automation task.

            ## Task

            - **ID**: {{ task.id }}
            - **Title**: {{ task.title }}

            ## Instructions

            {{ task.description }}

            When finished, call the **`CompleteLocalTask`** tool with a short summary.
            """;
        return $"""
            ---
            max_rounds: 10
            workspace: {workspaceYamlValue}
            ---
            """ + Body;
    }

    /// <summary>Returns <c>project</c> or <c>isolated</c> for workflow.md YAML.</summary>
    private static string NormalizeWorkspaceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "project";

        var m = mode.Trim();
        if (string.Equals(m, "project", StringComparison.OrdinalIgnoreCase))
            return "project";
        if (string.Equals(m, "isolated", StringComparison.OrdinalIgnoreCase))
            return "isolated";

        throw AppServerErrors.InvalidParams("'workspaceMode' must be 'project' or 'isolated'.");
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex SlugRegex();

    /// <summary>
    /// Validates that a path does not contain directory traversal sequences.
    /// </summary>
    private static bool ContainsDirectoryTraversal(string path)
    {
        // Check for parent directory traversal
        if (path.Contains("..", StringComparison.Ordinal))
            return true;

        // Check for null bytes (potential null byte injection)
        if (path.IndexOf('\0') >= 0)
            return true;

        return false;
    }

    /// <summary>
    /// Validates that a task ID is safe for use as a directory name.
    /// </summary>
    private static bool IsValidTaskId(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            return false;

        // Must not contain directory traversal
        if (ContainsDirectoryTraversal(taskId))
            return false;

        // Must not contain path separators
        if (taskId.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            return false;

        // Must not be a reserved Windows name
        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", 
            "LPT1", "LPT2", "LPT3", "LPT4" };
        if (reserved.Any(r => string.Equals(taskId, r, StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    /// <summary>
    /// Checks if a directory already exists to prevent accidental overwrites.
    /// </summary>
    private bool TaskDirectoryExists(string taskId)
    {
        var taskDir = Path.Combine(fileStore.TasksRoot, taskId);
        return Directory.Exists(taskDir);
    }

    #endregion
}
