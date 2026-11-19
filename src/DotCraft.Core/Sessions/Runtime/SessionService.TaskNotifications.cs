using System.Security;
using System.Security.Cryptography;
using System.Text;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService : IBackgroundTaskNotifications
{
    internal const string TerminalTriggerKind = "terminal";
    internal const string SubAgentTaskTriggerKind = "subagent";
    internal const string WorkflowTriggerKind = "workflow";
    private const string ThreadReportTriggerKind = "thread";

    public async Task<bool> NotifyThreadAsync(string threadId, ThreadTaskReport report, CancellationToken ct = default)
    {
        var ending = new BackgroundTaskEnding
        {
            TaskId = report.ReportedThreadId,
            Kind = ThreadReportTriggerKind,
            OwnerThreadId = threadId,
            Label = report.Title,
            Status = report.Status,
            KindStatus = report.Status,
            Summary = report.Summary,
            Result = report.Result
        };
        var reportQueueId = "queued_thread_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(report.NotificationId)));
        var (_, wake, added) = await InvokeThreadCommandAsync(threadId,
            commandCt => EnqueueTaskNotificationAsync(ending, commandCt, reportQueueId, report.Project),
            ct).ConfigureAwait(false);
        if (wake)
            await TryStartNextQueuedTurnAsync(threadId, ct).ConfigureAwait(false);
        return added;
    }

    public async Task<string?> NotifyBackgroundTaskEndedAsync(BackgroundTaskEnding ending, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ending);

        try
        {
            await GetOrLoadThreadAsync(ending.OwnerThreadId, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            logger?.LogInformation("Dropped notification for background task {TaskId}: thread {ThreadId} no longer exists",
                ending.TaskId, ending.OwnerThreadId);
            return null;
        }

        var (queuedId, wake, _) = await InvokeThreadCommandAsync(
            ending.OwnerThreadId,
            commandCt => EnqueueTaskNotificationAsync(ending, commandCt),
            ct).ConfigureAwait(false);
        if (wake)
            await InvokeThreadCommandAsync(
                ending.OwnerThreadId,
                commandCt => ThreadQueue.TryStartNextAsync(ending.OwnerThreadId, commandCt,
                    propagateFailure: ending.Kind == BackgroundTaskKinds.Workflow),
                ct).ConfigureAwait(false);
        return queuedId;
    }

    private async Task<(string QueuedId, bool Wake, bool Added)> EnqueueTaskNotificationAsync(
        BackgroundTaskEnding ending, CancellationToken ct, string? inputId = null, string? project = null)
    {
        var triggerKind = TaskNotificationTriggerKind(ending.Kind);
        if ((inputId != null || triggerKind == WorkflowTriggerKind)
            && await FindTaskNotificationAsync(ending, triggerKind, inputId, ct).ConfigureAwait(false) is { } existing)
            return (existing.QueuedId, existing.Wake, false);

        var displayText = BuildTaskNotificationDisplayText(ending);
        var (queued, thread, steered) = await EnqueueTriggeredInputAsync(
            ending.OwnerThreadId,
            new QueuedTurnInput
            {
                Id = inputId ?? SessionIdGenerator.NewQueuedInputId(),
                ThreadId = ending.OwnerThreadId,
                NativeInputParts = [new SessionInputPart { Type = "text", Text = displayText }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = BuildTaskNotificationModelText(ending, project) }],
                DisplayText = displayText,
                CreatedAt = DateTimeOffset.UtcNow,
                TriggerKind = triggerKind,
                TriggerLabel = ending.Label,
                TriggerRefId = ending.TaskId,
                TriggerStatus = ending.KindStatus
            },
            ct).ConfigureAwait(false);
        if (steered || !ending.MayWake || thread.Status != ThreadStatus.Active)
            return (queued.Id, false, true);
        var wake = MayWakeForTaskNotifications();
        if (!wake)
            logger?.LogInformation("Holding notification for background task {TaskId} on thread {ThreadId}", ending.TaskId, thread.Id);
        return (queued.Id, wake, true);
    }

    private async Task<(string QueuedId, bool Wake)?> FindTaskNotificationAsync(
        BackgroundTaskEnding ending, string triggerKind, string? inputId, CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(ending.OwnerThreadId, ct).ConfigureAwait(false);
        using var lease = await AcquireThreadQueueLockAsync(thread.Id, ct).ConfigureAwait(false);
        var pending = thread.QueuedInputs.FirstOrDefault(input =>
            input.TriggerKind == triggerKind && Matches(input.Id, input.TriggerRefId));
        if (pending != null)
            return (pending.Id, ending.MayWake && thread.Status == ThreadStatus.Active && MayWakeForTaskNotifications());

        await foreach (var turn in ReadTurnsAsync(thread, ThreadHistorySortDirection.Descending, includeItems: true, ct)
                           .ConfigureAwait(false))
        {
            foreach (var item in turn.Items)
            {
                if (item.AsUserMessage is { QueuedInputId: { } queuedId } message
                    && message.TriggerKind == triggerKind && Matches(queuedId, message.TriggerRefId))
                    return (queuedId, false);
            }
        }
        return null;

        bool Matches(string queuedId, string? refId) => inputId != null ? queuedId == inputId : refId == ending.TaskId;
    }

    private static string TaskNotificationTriggerKind(string kind) => kind switch
    {
        BackgroundTaskKinds.Shell => TerminalTriggerKind,
        BackgroundTaskKinds.Agent => SubAgentTaskTriggerKind,
        BackgroundTaskKinds.Workflow => WorkflowTriggerKind,
        ThreadReportTriggerKind => ThreadReportTriggerKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown background task kind.")
    };

    private static bool IsTaskNotification(QueuedTurnInput input) =>
        input.TriggerKind is TerminalTriggerKind or SubAgentTaskTriggerKind or WorkflowTriggerKind or ThreadReportTriggerKind
        && !string.IsNullOrEmpty(input.TriggerStatus);

    private bool MayWakeForTaskNotifications() =>
        string.Equals(
            (_appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config).Tools.BackgroundTasks.CompletionDelivery,
            "wake",
            StringComparison.OrdinalIgnoreCase);

    private static string BuildTaskNotificationDisplayText(BackgroundTaskEnding ending)
    {
        var outcome = ending.StoppedByUser ? "was stopped by the user"
            : ending.Status switch
            {
                BackgroundTaskStatus.Completed => "finished",
                BackgroundTaskStatus.Stopped => "was stopped",
                BackgroundTaskStatus.WaitingForInput => "is waiting for input",
                _ => "failed"
            };
        return ending.Kind switch
        {
            BackgroundTaskKinds.Shell when ending.Status == BackgroundTaskStatus.WaitingForInput =>
                $"Background command is waiting for input: {ending.Label}",
            BackgroundTaskKinds.Shell when ending.StoppedByUser => $"Background command stopped by the user: {ending.Label}",
            BackgroundTaskKinds.Shell =>
                $"Background command finished ({(ending.ExitCode is { } exitCode ? $"exit code {exitCode}" : ending.KindStatus)}): {ending.Label}",
            BackgroundTaskKinds.Agent => $"Subagent {ending.Label} {outcome}",
            ThreadReportTriggerKind => $"Chat {ending.Label} {outcome}",
            _ => $"Workflow {ending.Label} {outcome}"
        };
    }

    private static string BuildTaskNotificationModelText(BackgroundTaskEnding ending, string? project)
    {
        var isThreadReport = ending.Kind == ThreadReportTriggerKind;
        string Format(string text) => isThreadReport ? SecurityElement.Escape(text) : text;

        var sb = new StringBuilder();
        sb.AppendLine("<task_notification>");
        sb.AppendLine($"<task_id>{Format(ending.TaskId)}</task_id>");
        sb.AppendLine($"<status>{ending.Status}</status>");
        sb.AppendLine($"<summary>{Format(ending.Summary)}</summary>");
        if (!string.IsNullOrEmpty(project))
            sb.AppendLine($"<project>{Format(project)}</project>");
        if (!string.IsNullOrEmpty(ending.OutputPath))
            sb.AppendLine($"<output_file>{ending.OutputPath}</output_file>");
        if (!string.IsNullOrEmpty(ending.Result))
        {
            sb.AppendLine("<result>");
            sb.AppendLine(Format(ending.Result.TrimEnd()));
            sb.AppendLine("</result>");
        }
        sb.Append("</task_notification>");
        if (ending.Kind == BackgroundTaskKinds.Shell && ending.Status == BackgroundTaskStatus.WaitingForInput)
            sb.Append(ending.StdinOpen
                ? "\nAnswer the prompt with WriteStdin, or stop the task with TaskStop."
                : "\nIts stdin is closed. Stop the task with TaskStop, then rerun the command with piped input or a non-interactive option.");
        return sb.ToString();
    }
}
