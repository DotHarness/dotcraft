using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
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
    [Description("Manage scheduled work. Create and update take a complete definition; update also requires expectedVersion. Report records the current run's outcome.")]
    public async Task<string> Automation(
        [Description("Operation to perform.")] AutomationToolAction action,
        [Description("Existing automation identifier.")] string? automationId = null,
        [Description("Editable definition for create/update.")] AutomationToolInput? automation = null,
        [Description("Current version required for update.")] int? expectedVersion = null,
        [MaxLength(AutomationService.MaxOutcomeSummaryChars)]
        [Description("Optional report summary.")] string? summary = null,
        [Description("Whether a meaningful change occurred.")] bool? important = null,
        [MaxLength(AutomationService.MaxMemoryChars)]
        [Description("Replacement memory for later runs.")] string? memory = null,
        CancellationToken cancellationToken = default)
    {
        var context = ToolHostExecutionScope.Current;
        if (action == AutomationToolAction.Report)
        {
            if (context == null) throw new InvalidOperationException("automation.noActiveRun");
            service.ReportOutcome(context.ThreadId, context.TurnId, summary, important, memory);
            return JsonSerializer.Serialize(new { operation = ActionName(action), recorded = true }, AutomationStore.Json);
        }
        if (action == AutomationToolAction.List) return JsonSerializer.Serialize(new { operation = ActionName(action), automations = await service.ListAsync(cancellationToken) }, AutomationStore.Json);
        if (action == AutomationToolAction.Create)
        {
            if (automation == null) throw new ArgumentException("automation.definitionRequired");
            var input = Normalize(automation);
            if (input.ExecutionMode == "thread" && input.TargetThreadId == null)
                input = input with { TargetThreadId = context?.ThreadId ?? planningThreadId };
            var source = ChannelSessionScope.Current;
            var origin = source == null ? null : new AutomationOrigin { Channel = source.Channel, UserId = source.UserId, GroupId = source.GroupId, DeliveryTarget = source.DefaultDeliveryTarget };
            return Result(action, await service.CreateAsync(input, origin, cancellationToken));
        }
        if (string.IsNullOrWhiteSpace(automationId)) throw new ArgumentException("automation.idRequired");
        var current = await service.ReadAsync(automationId, cancellationToken);
        if (action == AutomationToolAction.Delete) { await service.DeleteAsync(automationId, cancellationToken); return Result(action, current); }
        if (action == AutomationToolAction.Run) return JsonSerializer.Serialize(new { operation = ActionName(action), automation = current,
            run = await service.RunAsync(automationId, cancellationToken) }, AutomationStore.Json);
        if (action == AutomationToolAction.Read) return Result(action, current);
        var next = action switch {
            AutomationToolAction.Update => Normalize(automation ?? throw new ArgumentException("automation.definitionRequired")),
            AutomationToolAction.Pause => current with { Status = "paused" }, AutomationToolAction.Resume => current with { Status = "active" },
            _ => throw new ArgumentException("automation.invalidAction") };
        var version = action == AutomationToolAction.Update ? expectedVersion ?? throw new ArgumentException("automation.expectedVersionRequired") : current.Version;
        return Result(action, await service.UpdateAsync(automationId, version, next, cancellationToken));
    }
    private static AutomationInput Normalize(AutomationToolInput input) => new()
    {
        Name = input.Name,
        Prompt = input.Prompt,
        Status = input.Status == AutomationToolStatus.Active ? "active" : "paused",
        ExecutionMode = input.ExecutionMode == AutomationToolExecutionMode.Thread ? "thread" : "independent",
        TargetThreadId = EmptyToNull(input.TargetThreadId),
        WorkspaceMode = input.WorkspaceMode switch { AutomationToolWorkspaceMode.Project => "project", AutomationToolWorkspaceMode.Worktree => "worktree", _ => null },
        AgentProfileId = EmptyToNull(input.AgentProfileId),
        ApprovalPolicy = input.ApprovalPolicy == AutomationToolApprovalPolicy.FullAuto ? "fullAuto" : "workspaceScope",
        Schedule = Normalize(input.Schedule),
        NotificationPolicy = input.NotificationPolicy switch { AutomationToolNotificationPolicy.Important => "important", AutomationToolNotificationPolicy.All => "all", AutomationToolNotificationPolicy.Failures => "failures", _ => null }
    };
    private static AutomationSchedule Normalize(AutomationToolSchedule schedule)
    {
        var kind = schedule.Kind switch
        {
            AutomationToolScheduleKind.At => "at",
            AutomationToolScheduleKind.Every => "every",
            AutomationToolScheduleKind.Daily => "daily",
            AutomationToolScheduleKind.Weekdays => "weekdays",
            AutomationToolScheduleKind.Weekly => "weekly",
            _ => throw new ArgumentException("automation.schedule.invalidKind")
        };
        return schedule.Kind switch
        {
            AutomationToolScheduleKind.At => new() { Kind = kind, At = schedule.At },
            AutomationToolScheduleKind.Every => new() { Kind = kind, EveryMs = schedule.EveryMs },
            AutomationToolScheduleKind.Daily or AutomationToolScheduleKind.Weekdays => new()
            {
                Kind = kind,
                Hour = schedule.Hour,
                Minute = schedule.Minute,
                TimeZone = EmptyToNull(schedule.TimeZone)
            },
            AutomationToolScheduleKind.Weekly => new()
            {
                Kind = kind,
                Hour = schedule.Hour,
                Minute = schedule.Minute,
                TimeZone = EmptyToNull(schedule.TimeZone),
                Days = schedule.Days
            },
            _ => throw new ArgumentException("automation.schedule.invalidKind")
        };
    }
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Result(AutomationToolAction action, AutomationDefinition? automation) => JsonSerializer.Serialize(new { operation = ActionName(action), automation }, AutomationStore.Json);
    private static string ActionName(AutomationToolAction action) => action switch
    {
        AutomationToolAction.List => "list", AutomationToolAction.Read => "read", AutomationToolAction.Create => "create",
        AutomationToolAction.Update => "update", AutomationToolAction.Pause => "pause", AutomationToolAction.Resume => "resume",
        AutomationToolAction.Delete => "delete", AutomationToolAction.Run => "run", AutomationToolAction.Report => "report",
        _ => throw new ArgumentException("automation.invalidAction")
    };
}
