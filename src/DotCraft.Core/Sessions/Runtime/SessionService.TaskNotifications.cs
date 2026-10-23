using System.Collections.Concurrent;
using System.Text;
using DotCraft.Sessions.Wire;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService : IBackgroundTaskNotifications
{
    internal const string TerminalTriggerKind = "terminal";
    internal const string SubAgentTaskTriggerKind = "subagent";
    internal const string WorkflowTriggerKind = "workflow";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _taskStartingTurns =
        new(StringComparer.Ordinal);

    public async Task<string?> NotifyBackgroundTaskEndedAsync(BackgroundTaskEnding ending, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ending);
        if (!string.IsNullOrWhiteSpace(ending.StartTurnId))
            _taskStartingTurns.GetOrAdd(ending.OwnerThreadId, static _ => new(StringComparer.Ordinal))
                .TryAdd(ending.StartTurnId, 0);

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

        var (queuedId, wake) = await InvokeThreadCommandAsync(
            ending.OwnerThreadId,
            commandCt => EnqueueTaskNotificationAsync(ending, commandCt),
            ct).ConfigureAwait(false);
        if (wake)
            await TryStartNextQueuedTurnAsync(ending.OwnerThreadId, CancellationToken.None).ConfigureAwait(false);
        return queuedId;
    }

    private async Task<(string QueuedId, bool Wake)> EnqueueTaskNotificationAsync(BackgroundTaskEnding ending, CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(ending.OwnerThreadId, ct).ConfigureAwait(false);
        var displayText = BuildTaskNotificationDisplayText(ending);
        QueuedTurnInput queued;
        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        SessionTurn? activeTurn;
        using (await AcquireThreadQueueLockAsync(ending.OwnerThreadId, ct).ConfigureAwait(false))
        {
            if (_runtimeRegistry.TryGetThread(ending.OwnerThreadId, out var cachedThread))
                thread = cachedThread;

            activeTurn = thread.Status == ThreadStatus.Active
                ? thread.Turns.LastOrDefault(static turn =>
                    turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput)
                : null;
            queued = new QueuedTurnInput
            {
                Id = SessionIdGenerator.NewQueuedInputId(),
                ThreadId = thread.Id,
                NativeInputParts = [new SessionInputPart { Type = "text", Text = displayText }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = BuildTaskNotificationModelText(ending) }],
                DisplayText = displayText,
                Status = activeTurn == null ? "queued" : "guidancePending",
                CreatedAt = DateTimeOffset.UtcNow,
                ReadyAfterTurnId = activeTurn?.Id,
                TriggerKind = TaskNotificationTriggerKind(ending.Kind),
                TriggerLabel = ending.Label,
                TriggerRefId = ending.TaskId,
                TriggerStatus = ending.KindStatus
            };
            var queue = thread.QueuedInputs.ToList();
            queue.Add(queued);
            thread.QueuedInputs = queue;
            await PersistThreadChangesAsync(thread, [RolloutChangeRecords.QueuedInputAdded(thread.Id, queued)], ct).ConfigureAwait(false);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        if (activeTurn != null)
        {
            var source = thread.Source.SubAgent;
            _subAgentCommunicationRuntime.PublishSteer(
                string.IsNullOrWhiteSpace(source?.RootThreadId) ? thread.Id : source.RootThreadId,
                string.IsNullOrWhiteSpace(source?.AgentPath) ? AgentPath.Root : source.AgentPath);
            return (queued.Id, false);
        }

        if (!ending.MayWake || thread.Status != ThreadStatus.Active)
            return (queued.Id, false);
        var wake = await MayWakeForTaskNotificationsAsync(thread, ct).ConfigureAwait(false);
        if (!wake)
            logger?.LogInformation("Holding notification for background task {TaskId} on thread {ThreadId}", ending.TaskId, thread.Id);
        return (queued.Id, wake);
    }

    private static string TaskNotificationTriggerKind(string kind) => kind switch
    {
        BackgroundTaskKinds.Shell => TerminalTriggerKind,
        BackgroundTaskKinds.Agent => SubAgentTaskTriggerKind,
        BackgroundTaskKinds.Workflow => WorkflowTriggerKind,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown background task kind.")
    };

    private static bool IsTaskNotification(QueuedTurnInput input) =>
        input.TriggerKind is TerminalTriggerKind or SubAgentTaskTriggerKind or WorkflowTriggerKind
        && !string.IsNullOrEmpty(input.TriggerStatus);

    private static bool IsTaskNotificationTurn(SessionTurn turn) =>
        turn.Input?.AsUserMessage is { TriggerKind: TerminalTriggerKind or SubAgentTaskTriggerKind or WorkflowTriggerKind } message
        && !string.IsNullOrEmpty(message.TriggerStatus);

    private async Task<bool> MayWakeForTaskNotificationsAsync(SessionThread thread, CancellationToken ct)
    {
        var settings = (_appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config).Tools.BackgroundTasks;
        if (!string.Equals(settings.CompletionDelivery, "wake", StringComparison.OrdinalIgnoreCase))
            return false;

        var limit = Math.Max(0, settings.MaxConsecutiveWakes);
        if (limit == 0)
            return false;
        _taskStartingTurns.TryGetValue(thread.Id, out var startingTurns);
        var loopTurns = 0;
        await foreach (var turn in ReadTurnsAsync(thread, ThreadHistorySortDirection.Descending, includeItems: false, ct)
                           .ConfigureAwait(false))
        {
            if (!IsTaskNotificationTurn(turn) || startingTurns?.ContainsKey(turn.Id) != true)
                break;
            if (++loopTurns >= limit)
                return false;
        }

        return true;
    }

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
            _ => $"Workflow {ending.Label} {outcome}"
        };
    }

    private static string BuildTaskNotificationModelText(BackgroundTaskEnding ending)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<task_notification>");
        sb.AppendLine($"<task_id>{ending.TaskId}</task_id>");
        sb.AppendLine($"<status>{ending.Status}</status>");
        sb.AppendLine($"<summary>{ending.Summary}</summary>");
        if (!string.IsNullOrEmpty(ending.OutputPath))
            sb.AppendLine($"<output_file>{ending.OutputPath}</output_file>");
        if (!string.IsNullOrEmpty(ending.Result))
        {
            sb.AppendLine("<result>");
            sb.AppendLine(ending.Result.TrimEnd());
            sb.AppendLine("</result>");
        }
        sb.Append("</task_notification>");
        if (ending.Status == BackgroundTaskStatus.WaitingForInput)
            sb.Append(ending.StdinOpen
                ? "\nAnswer the prompt with WriteStdin, or stop the task with TaskStop."
                : "\nIts stdin is closed. Stop the task with TaskStop, then rerun the command with piped input or a non-interactive option.");
        return sb.ToString();
    }
}
