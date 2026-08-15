using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Templates;
using DotCraft.Cron;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;

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

    public async Task<Contract.AutomationTaskListResult> HandleTaskListAsync(
        Contract.AutomationTaskListParams parameters,
        CancellationToken ct)
    {
        _ = parameters;
        var tasks = await orchestrator.GetAllTasksAsync(ct);

        return new Contract.AutomationTaskListResult
        {
            Tasks = tasks.Select(ToWire).ToList()
        };
    }

    public async Task<Contract.AutomationTask> HandleTaskReadAsync(
        Contract.AutomationTaskReadParams parameters,
        CancellationToken ct)
    {
        var taskId = Read(parameters.TaskId) ?? string.Empty;
        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, taskId, StringComparison.Ordinal));

        if (task == null)
            throw AppServerErrors.TaskNotFound(taskId);

        return ToWireDetailed(task);
    }

    public Task<Contract.AutomationTaskCreateResult> HandleTaskCreateAsync(
        Contract.AutomationTaskCreateParams parameters,
        CancellationToken ct)
    {
        _ = ct;
        var title = Read(parameters.Title) ?? string.Empty;
        var descriptionValue = Read(parameters.Description);
        var templateId = Read(parameters.TemplateId);
        if (string.IsNullOrWhiteSpace(title))
            throw AppServerErrors.InvalidParams("'title' is required.");

        // Validate title length
        if (title.Length > 200)
            throw AppServerErrors.InvalidParams("'title' must be 200 characters or less.");

        // Validate description length
        if (descriptionValue != null && descriptionValue.Length > 10000)
            throw AppServerErrors.InvalidParams("'description' must be 10000 characters or less.");

        var taskId = GenerateTaskId(title, templateId);

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
        var description = descriptionValue ?? "";
        var approvalPolicyValue = Read(parameters.ApprovalPolicy);
        var approvalPolicy = string.IsNullOrWhiteSpace(approvalPolicyValue)
            ? "workspaceScope"
            : approvalPolicyValue.Trim();

        var fm = new StringBuilder();
        fm.AppendLine("---");
        fm.AppendLine($"id: \"{taskId}\"");
        fm.AppendLine($"title: \"{EscapeYamlString(title)}\"");
        fm.AppendLine("status: pending");
        fm.AppendLine($"created_at: \"{now}\"");
        fm.AppendLine($"updated_at: \"{now}\"");
        fm.AppendLine("thread_id: null");
        fm.AppendLine("agent_summary: null");
        fm.AppendLine($"approval_policy: \"{EscapeYamlString(approvalPolicy)}\"");

        var agentProfileId = Read(parameters.AgentProfileId);
        if (!string.IsNullOrWhiteSpace(agentProfileId))
            fm.AppendLine($"agent_profile_id: \"{EscapeYamlString(agentProfileId.Trim())}\"");

        if (!string.IsNullOrWhiteSpace(templateId))
            fm.AppendLine($"template_id: \"{EscapeYamlString(templateId)}\"");

        AppendScheduleYaml(fm, Read(parameters.Schedule));
        AppendThreadBindingYaml(fm, Read(parameters.ThreadBinding));

        fm.AppendLine("---");
        fm.AppendLine();
        fm.Append(description);

        File.WriteAllText(Path.Combine(taskDir, "task.md"), fm.ToString());

        var workflowWorkspaceMode = NormalizeOptionalWorkspaceMode(Read(parameters.WorkspaceMode));
        var workflowTemplate = Read(parameters.WorkflowTemplate);
        var workflowContent = string.IsNullOrWhiteSpace(workflowTemplate)
            ? BuildDefaultWorkflowContent(workflowWorkspaceMode ?? AutomationWorkspaceModeNames.Project)
            : ApplyExplicitWorkflowWorkspaceMode(
                workflowTemplate,
                workflowWorkspaceMode);
        File.WriteAllText(Path.Combine(taskDir, "workflow.md"), workflowContent);

        return Task.FromResult(new Contract.AutomationTaskCreateResult
        {
            TaskId = taskId,
            TaskDirectory = taskDir
        });
    }

    public async Task<Contract.AutomationTaskUpdateBindingResult> HandleTaskUpdateBindingAsync(
        Contract.AutomationTaskUpdateBindingParams parameters,
        CancellationToken ct)
    {
        var taskId = Read(parameters.TaskId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(taskId))
            throw AppServerErrors.InvalidParams("'taskId' is required.");

        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, taskId, StringComparison.Ordinal))
            as LocalAutomationTask;

        if (task == null)
            throw AppServerErrors.TaskNotFound(taskId);

        // Safety: don't rebind a task that is currently running; the frontend should confirm first.
        if (task.Status == AutomationTaskStatus.Running)
            throw AppServerErrors.TaskInvalidStatus(
                "Cannot change binding while the task is running. Cancel the run first.");

        var threadBinding = Read(parameters.ThreadBinding);
        var bindingThreadId = threadBinding is null ? null : Read(threadBinding.ThreadId);
        var bindingMode = threadBinding is null ? null : Read(threadBinding.Mode);
        task.ThreadBinding = threadBinding == null || string.IsNullOrWhiteSpace(bindingThreadId)
            ? null
            : new AutomationThreadBinding
            {
                ThreadId = bindingThreadId,
                Mode = string.IsNullOrWhiteSpace(bindingMode) ? "run-in-thread" : bindingMode
            };

        await fileStore.SaveAsync(task, ct);

        return new Contract.AutomationTaskUpdateBindingResult { Task = ToWireDetailed(task) };
    }

    public async Task<Contract.AutomationTaskRunResult> HandleTaskRunAsync(
        Contract.AutomationTaskRunParams parameters,
        CancellationToken ct)
    {
        var taskId = Read(parameters.TaskId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(taskId))
            throw AppServerErrors.InvalidParams("'taskId' is required.");

        var tasks = await orchestrator.GetAllTasksAsync(ct);
        var task = tasks.FirstOrDefault(t =>
            string.Equals(t.Id, taskId, StringComparison.Ordinal))
            as LocalAutomationTask;

        if (task == null)
            throw AppServerErrors.TaskNotFound(taskId);

        if (task.Status == AutomationTaskStatus.Running)
            throw AppServerErrors.TaskInvalidStatus(
                "Cannot run a task that is already running.");

        task.Status = AutomationTaskStatus.Pending;
        task.NextRunAt = task.Schedule == null
            ? null
            : DateTimeOffset.UtcNow.AddMilliseconds(-1);
        await fileStore.SaveAsync(task, ct);

        _ = orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

        return new Contract.AutomationTaskRunResult { Task = ToWireDetailed(task) };
    }

    public async Task<Contract.AutomationTaskDiscardWorktreeResult> HandleTaskDiscardWorktreeAsync(
        Contract.AutomationTaskDiscardWorktreeParams parameters,
        CancellationToken ct)
    {
        var taskId = Read(parameters.TaskId) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(taskId))
            throw AppServerErrors.InvalidParams("'taskId' is required.");

        try
        {
            var task = await orchestrator.DiscardTaskWorktreeAsync(taskId, ct);
            return new Contract.AutomationTaskDiscardWorktreeResult { Task = ToWireDetailed(task) };
        }
        catch (KeyNotFoundException)
        {
            throw AppServerErrors.TaskNotFound(taskId);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }
    }

    public async Task<Contract.AutomationTemplateListResult> HandleTemplateListAsync(
        Contract.AutomationTemplateListParams parameters,
        CancellationToken ct)
    {
        var templates = new List<Contract.AutomationTemplate>();
        // Built-ins first so the UI can keep its existing ordering; user templates follow with IsUser=true.
        foreach (var t in LocalTaskTemplates.ForLocale(Read(parameters.Locale)))
            templates.Add(ToWire(t));

        var user = await userTemplateStore.LoadAllAsync(ct);
        foreach (var t in user)
            templates.Add(ToWire(t));

        return new Contract.AutomationTemplateListResult { Templates = templates };
    }

    public async Task<Contract.AutomationTemplateSaveResult> HandleTemplateSaveAsync(
        Contract.AutomationTemplateSaveParams parameters,
        CancellationToken ct)
    {
        var title = Read(parameters.Title) ?? string.Empty;
        var workflowMarkdown = Read(parameters.WorkflowMarkdown) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
            throw AppServerErrors.InvalidParams("'title' is required.");
        if (title.Length > 200)
            throw AppServerErrors.InvalidParams("'title' must be 200 characters or less.");
        if (string.IsNullOrWhiteSpace(workflowMarkdown))
            throw AppServerErrors.InvalidParams("'workflowMarkdown' is required.");

        var requestedId = Read(parameters.Id);
        var id = string.IsNullOrWhiteSpace(requestedId) ? GenerateUserTemplateId() : requestedId.Trim();
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
                title: title,
                description: Read(parameters.Description),
                icon: Read(parameters.Icon),
                category: Read(parameters.Category),
                workflowMarkdown: workflowMarkdown,
                defaultSchedule: FromWire(Read(parameters.DefaultSchedule)),
                defaultWorkspaceMode: NormalizeOptionalWorkspaceMode(Read(parameters.DefaultWorkspaceMode)),
                defaultApprovalPolicy: Read(parameters.DefaultApprovalPolicy),
                defaultAgentProfileId: Read(parameters.DefaultAgentProfileId),
                needsThreadBinding: Read(parameters.NeedsThreadBinding),
                defaultTitle: Read(parameters.DefaultTitle),
                defaultDescription: Read(parameters.DefaultDescription),
                ct: ct);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        return new Contract.AutomationTemplateSaveResult { Template = ToWire(saved) };
    }

    public async Task<Contract.AutomationTemplateDeleteResult> HandleTemplateDeleteAsync(
        Contract.AutomationTemplateDeleteParams parameters,
        CancellationToken ct)
    {
        var id = Read(parameters.Id) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(id))
            throw AppServerErrors.InvalidParams("'id' is required.");
        if (LocalTaskTemplates.FindById(id) != null)
            throw AppServerErrors.InvalidParams(
                $"Template id '{id}' is a built-in template and cannot be deleted.");
        if (!UserTemplateFileStore.IsValidId(id))
            throw AppServerErrors.InvalidParams("'id' has an invalid shape.");

        try
        {
            await userTemplateStore.DeleteAsync(id, ct);
        }
        catch (ArgumentException ex)
        {
            throw AppServerErrors.InvalidParams(ex.Message);
        }

        return new Contract.AutomationTemplateDeleteResult { Ok = true };
    }

    private static Contract.AutomationTemplate ToWire(LocalTaskTemplate t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Description = string.IsNullOrWhiteSpace(t.Description) ? default : OmitIfNull(t.Description),
        Icon = string.IsNullOrWhiteSpace(t.Icon) ? default : OmitIfNull(t.Icon),
        Category = string.IsNullOrWhiteSpace(t.Category) ? default : OmitIfNull(t.Category),
        WorkflowMarkdown = t.WorkflowMarkdown,
        DefaultSchedule = ToWire(t.DefaultSchedule) is { } schedule
            ? DotCraft.Protocol.Optional<Contract.AutomationSchedule?>.FromValue(schedule)
            : default,
        DefaultWorkspaceMode = OmitIfNull(NormalizeOptionalWorkspaceMode(t.DefaultWorkspaceMode)),
        DefaultApprovalPolicy = OmitIfNull(t.DefaultApprovalPolicy),
        DefaultAgentProfileId = string.IsNullOrWhiteSpace(t.DefaultAgentProfileId) ? default : OmitIfNull(t.DefaultAgentProfileId),
        NeedsThreadBinding = DotCraft.Protocol.Optional<bool?>.FromValue(t.NeedsThreadBinding),
        DefaultTitle = OmitIfNull(t.DefaultTitle),
        DefaultDescription = OmitIfNull(t.DefaultDescription),
        IsUser = t.IsUser ? true : default,
        CreatedAt = OmitIfNull(t.CreatedAt),
        UpdatedAt = OmitIfNull(t.UpdatedAt)
    };

    private static CronSchedule? FromWire(Contract.AutomationSchedule? wire)
    {
        var kindValue = wire is null ? null : Read(wire.Kind);
        if (wire == null || string.IsNullOrWhiteSpace(kindValue))
            return null;
        var kind = kindValue.Trim().ToLowerInvariant();
        if (kind == "once")
            return null;
        return new CronSchedule
        {
            Kind = kind,
            AtMs = Read(wire.AtMs),
            EveryMs = Read(wire.EveryMs),
            InitialDelayMs = Read(wire.InitialDelayMs),
            DailyHour = Read(wire.DailyHour),
            DailyMinute = Read(wire.DailyMinute),
            Expr = Read(wire.Expr),
            Tz = Read(wire.Tz)
        };
    }

    private static string GenerateUserTemplateId() =>
        "user-" + Guid.NewGuid().ToString("N")[..10];

    private static void AppendScheduleYaml(StringBuilder sb, Contract.AutomationSchedule? schedule)
    {
        var kindValue = schedule is null ? null : Read(schedule.Kind);
        if (schedule == null || string.IsNullOrWhiteSpace(kindValue))
            return;
        var kind = kindValue.Trim().ToLowerInvariant();
        if (kind == "once")
            return;
        sb.AppendLine("schedule:");
        sb.AppendLine($"  kind: \"{EscapeYamlString(kind)}\"");
        if (Read(schedule.AtMs) is { } atMs) sb.AppendLine($"  at_ms: {atMs}");
        if (Read(schedule.EveryMs) is { } everyMs) sb.AppendLine($"  every_ms: {everyMs}");
        if (Read(schedule.InitialDelayMs) is { } initialDelayMs) sb.AppendLine($"  initial_delay_ms: {initialDelayMs}");
        if (Read(schedule.DailyHour) is { } dailyHour) sb.AppendLine($"  daily_hour: {dailyHour}");
        if (Read(schedule.DailyMinute) is { } dailyMinute) sb.AppendLine($"  daily_minute: {dailyMinute}");
        if (Read(schedule.Expr) is { Length: > 0 } expr) sb.AppendLine($"  expr: \"{EscapeYamlString(expr)}\"");
        if (Read(schedule.Tz) is { Length: > 0 } tz) sb.AppendLine($"  tz: \"{EscapeYamlString(tz)}\"");
    }

    private static void AppendThreadBindingYaml(StringBuilder sb, Contract.AutomationThreadBinding? binding)
    {
        var threadId = binding is null ? null : Read(binding.ThreadId);
        if (binding == null || string.IsNullOrWhiteSpace(threadId))
            return;
        sb.AppendLine("thread_binding:");
        sb.AppendLine($"  thread_id: \"{EscapeYamlString(threadId)}\"");
        var modeValue = Read(binding.Mode);
        var mode = string.IsNullOrWhiteSpace(modeValue) ? "run-in-thread" : modeValue.Trim();
        sb.AppendLine($"  mode: \"{EscapeYamlString(mode)}\"");
    }

    public async Task<Contract.AutomationTaskDeleteResult> HandleTaskDeleteAsync(
        Contract.AutomationTaskDeleteParams parameters,
        CancellationToken ct)
    {
        var taskId = Read(parameters.TaskId) ?? string.Empty;
        try
        {
            await orchestrator.DeleteTaskAsync(taskId, ct);
        }
        catch (KeyNotFoundException)
        {
            throw AppServerErrors.TaskNotFound(taskId);
        }
        catch (NotSupportedException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.TaskInvalidStatus(ex.Message);
        }

        return new Contract.AutomationTaskDeleteResult { Ok = true };
    }

    #region Helpers

    private static Contract.AutomationTask ToWire(AutomationTask task) => BuildWire(task, detailed: false);

    private static Contract.AutomationTask ToWireDetailed(AutomationTask task) => BuildWire(task, detailed: true);

    private static Contract.AutomationTask BuildWire(AutomationTask task, bool detailed)
    {
        var local = task as LocalAutomationTask;
        Contract.AutomationTaskWorktree? worktree = null;
        if (local is
            {
                ThreadBinding: null,
                WorkspaceMode: AutomationWorkspaceMode.Worktree,
                Worktree: not null
            })
        {
            worktree = new Contract.AutomationTaskWorktree
            {
                BranchName = local.Worktree.BranchName,
                Path = local.Worktree.Path
            };
        }

        return new Contract.AutomationTask
        {
            Id = task.Id,
            Title = task.Title,
            Status = StatusToWire(task.Status),
            ThreadId = OmitIfNull(task.ThreadId),
            Description = detailed ? OmitIfNull(task.Description) : default,
            AgentSummary = detailed ? OmitIfNull(task.AgentSummary) : default,
            CreatedAt = OmitIfNull(task.CreatedAt),
            UpdatedAt = OmitIfNull(task.UpdatedAt),
            ApprovalPolicy = local is null ? default : OmitIfNull(local.ApprovalPolicy),
            AgentProfileId = local is null || string.IsNullOrWhiteSpace(local.AgentProfileId)
                ? default
                : OmitIfNull(local.AgentProfileId),
            WorkspaceMode = local is null
                ? AutomationWorkspaceModeNames.Project
                : AutomationWorkspaceModeNames.ToCanonicalString(local.WorkspaceMode),
            Worktree = worktree is null
                ? default
                : DotCraft.Protocol.Optional<Contract.AutomationTaskWorktree?>.FromValue(worktree),
            Schedule = ToWire(task.Schedule) is { } schedule
                ? DotCraft.Protocol.Optional<Contract.AutomationSchedule?>.FromValue(schedule)
                : default,
            ThreadBinding = ToWire(task.ThreadBinding) is { } binding
                ? DotCraft.Protocol.Optional<Contract.AutomationThreadBinding?>.FromValue(binding)
                : default,
            NextRunAt = OmitIfNull(task.NextRunAt)
        };
    }

    private static Contract.AutomationSchedule? ToWire(CronSchedule? schedule)
    {
        if (schedule == null)
            return null;
        return new Contract.AutomationSchedule
        {
            Kind = schedule.Kind,
            AtMs = OmitIfNull(schedule.AtMs),
            EveryMs = OmitIfNull(schedule.EveryMs),
            InitialDelayMs = OmitIfNull(schedule.InitialDelayMs),
            DailyHour = OmitIfNull(schedule.DailyHour),
            DailyMinute = OmitIfNull(schedule.DailyMinute),
            Expr = OmitIfNull(schedule.Expr),
            Tz = OmitIfNull(schedule.Tz)
        };
    }

    private static Contract.AutomationThreadBinding? ToWire(AutomationThreadBinding? binding)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.ThreadId))
            return null;
        return new Contract.AutomationThreadBinding
        {
            ThreadId = binding.ThreadId,
            Mode = OmitIfNull(binding.Mode)
        };
    }

    /// <summary>
    /// Converts an automation task to the public notification contract.
    /// for use in <c>automation/task/updated</c> notifications.
    /// </summary>
    public static Contract.AutomationTask ToNotificationWire(AutomationTask task) => ToWire(task);

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

    private static T? Read<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);

    /// <summary>
    /// <paramref name="workspaceYamlValue"/> is <c>project</c> or <c>worktree</c> (validated by <see cref="NormalizeWorkspaceMode"/>).
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

    /// <summary>Returns <c>project</c> or <c>worktree</c> for workflow.md YAML.</summary>
    private static string NormalizeWorkspaceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return AutomationWorkspaceModeNames.Project;

        if (AutomationWorkspaceModeNames.TryNormalize(mode, out var normalized) && normalized != null)
            return normalized;

        throw AppServerErrors.InvalidParams("'workspaceMode' must be 'project' or 'worktree'.");
    }

    private static string? NormalizeOptionalWorkspaceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return null;

        if (AutomationWorkspaceModeNames.TryNormalize(mode, out var normalized))
            return normalized;

        throw AppServerErrors.InvalidParams("'defaultWorkspaceMode' must be 'project' or 'worktree'.");
    }

    private static string ApplyExplicitWorkflowWorkspaceMode(string markdown, string? workspaceMode)
    {
        if (string.IsNullOrWhiteSpace(workspaceMode) || string.IsNullOrWhiteSpace(markdown))
            return markdown;

        var newline = markdown.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var match = WorkflowFrontMatterRegex().Match(markdown);
        if (!match.Success)
        {
            return $"---{newline}workspace: {workspaceMode}{newline}---{newline}{markdown}";
        }

        var yamlGroup = match.Groups["yaml"];
        var yaml = yamlGroup.Value;
        var workspaceLineRegex = WorkflowWorkspaceLineRegex();
        var updatedYaml = workspaceLineRegex.IsMatch(yaml)
            ? workspaceLineRegex.Replace(
                yaml,
                m =>
                {
                    var suffix = m.Groups[2].Value;
                    if (suffix.StartsWith("#", StringComparison.Ordinal))
                        suffix = " " + suffix;
                    return $"{m.Groups[1].Value}{workspaceMode}{suffix}";
                },
                1)
            : $"workspace: {workspaceMode}{newline}{yaml}";

        return markdown[..yamlGroup.Index] + updatedYaml + markdown[(yamlGroup.Index + yamlGroup.Length)..];
    }

    [GeneratedRegex(@"\A---[ \t]*\r?\n(?<yaml>.*?)(^---[ \t]*\r?\n?)", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex WorkflowFrontMatterRegex();

    [GeneratedRegex(@"(?im)^(workspace\s*:\s*)[""']?[^#\r\n""']*[""']?(\s*(?:#.*)?$)")]
    private static partial Regex WorkflowWorkspaceLineRegex();

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

    #endregion
}
