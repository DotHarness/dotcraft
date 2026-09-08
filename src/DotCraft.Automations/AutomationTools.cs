using System.ComponentModel;
using System.Text.Json;
using DotCraft.Channels;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
namespace DotCraft.Automations;

/// <summary>Trusted module-owned automation source.</summary>
public sealed class AutomationToolSource(AutomationService service) : AIFunctionToolSource
{
    public override string SourceId => "automations";
    protected override IEnumerable<AIFunction> CreateFunctions(ToolPlanningContext context) =>
        [DotCraft.GeneratedTools.Automations.GeneratedToolFunctions.AutomationTools_Automation(new AutomationTools(service, context.ThreadId))];
    protected override ToolPresentationDescriptor? GetPresentation(AIFunction function, ToolPlanningContext context) => new(new PresentationId("core.automation"));
}
/// <summary>Conversation-driven automation lifecycle.</summary>
public sealed class AutomationTools(AutomationService service, string? planningThreadId = null)
{
    [GeneratedTool]
    [Tool(Icon = "⏰")]
    [Description("Manage scheduled work. For create/update, pass the complete definition inside automation. Use thread mode for follow-ups and independent for standalone work. Updates require expectedVersion. Report records optional summary, importance, and memory for the current run.")]
    public async Task<string> Automation(
        [Description("Operation: list, read, create, update, pause, resume, delete, run, or report.")] string action,
        [Description("Existing automation identifier.")] string? automationId = null,
        [Description("Editable definition for create/update.")] AutomationInput? automation = null,
        [Description("Current version required for update.")] int? expectedVersion = null,
        [Description("Optional report summary.")] string? summary = null,
        [Description("Whether a meaningful change occurred.")] bool? important = null,
        [Description("Replacement memory for later runs.")] string? memory = null,
        CancellationToken cancellationToken = default)
    {
        var context = ToolHostExecutionScope.Current;
        if (action == "report")
        {
            if (context == null) throw new InvalidOperationException("automation.noActiveRun");
            service.ReportOutcome(context.ThreadId, context.TurnId, summary, important, memory);
            return JsonSerializer.Serialize(new { operation = action, recorded = true }, AutomationStore.Json);
        }
        if (action == "list") return JsonSerializer.Serialize(new { operation = action, automations = await service.ListAsync(cancellationToken) }, AutomationStore.Json);
        if (action == "create")
        {
            if (automation == null) throw new ArgumentException("automation.definitionRequired");
            automation = Normalize(automation);
            if (automation.ExecutionMode == "thread" && automation.TargetThreadId == null)
                automation = automation with { TargetThreadId = context?.ThreadId ?? planningThreadId };
            var source = ChannelSessionScope.Current;
            var origin = source == null ? null : new AutomationOrigin { Channel = source.Channel, UserId = source.UserId, GroupId = source.GroupId, DeliveryTarget = source.DefaultDeliveryTarget };
            return Result(action, await service.CreateAsync(automation, origin, cancellationToken));
        }
        if (string.IsNullOrWhiteSpace(automationId)) throw new ArgumentException("automation.idRequired");
        var current = await service.ReadAsync(automationId, cancellationToken);
        if (action == "delete") { await service.DeleteAsync(automationId, cancellationToken); return Result(action, current); }
        if (action == "run") return JsonSerializer.Serialize(new { operation = action, automation = current,
            run = await service.RunAsync(automationId, cancellationToken) }, AutomationStore.Json);
        if (action == "read") return Result(action, current);
        var next = action switch {
            "update" => Normalize(automation ?? throw new ArgumentException("automation.definitionRequired")),
            "pause" => current with { Status = "paused" }, "resume" => current with { Status = "active" },
            _ => throw new ArgumentException("automation.invalidAction") };
        var version = action == "update" ? expectedVersion ?? throw new ArgumentException("automation.expectedVersionRequired") : current.Version;
        return Result(action, await service.UpdateAsync(automationId, version, next, cancellationToken));
    }
    private static AutomationInput Normalize(AutomationInput input) => input with
    {
        Status = input.Status?.Trim() ?? "",
        ExecutionMode = input.ExecutionMode?.Trim() ?? "",
        TargetThreadId = EmptyToNull(input.TargetThreadId),
        WorkspaceMode = EmptyToNull(input.WorkspaceMode),
        AgentProfileId = EmptyToNull(input.AgentProfileId),
        ApprovalPolicy = string.IsNullOrWhiteSpace(input.ApprovalPolicy) ? "workspaceScope" : input.ApprovalPolicy.Trim(),
        Schedule = Normalize(input.Schedule ?? throw new ArgumentException("automation.scheduleRequired")),
        NotificationPolicy = EmptyToNull(input.NotificationPolicy)
    };
    private static AutomationSchedule Normalize(AutomationSchedule schedule)
    {
        var kind = schedule.Kind?.Trim() ?? "";
        return kind switch
        {
            "at" => new() { Kind = kind, At = schedule.At },
            "every" => new() { Kind = kind, EveryMs = schedule.EveryMs },
            "daily" or "weekdays" => new()
            {
                Kind = kind,
                Hour = schedule.Hour,
                Minute = schedule.Minute,
                TimeZone = EmptyToNull(schedule.TimeZone)
            },
            "weekly" => new()
            {
                Kind = kind,
                Hour = schedule.Hour,
                Minute = schedule.Minute,
                TimeZone = EmptyToNull(schedule.TimeZone),
                Days = schedule.Days
            },
            _ => schedule with { Kind = kind }
        };
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Result(string operation, AutomationDefinition? automation) => JsonSerializer.Serialize(new { operation, automation }, AutomationStore.Json);
}
