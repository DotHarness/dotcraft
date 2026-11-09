using System.Text.Json.Nodes;
using DotCraft.Sessions;
using Microsoft.Extensions.Logging;

namespace DotCraft.DynamicWorkflows;

public sealed partial class DynamicWorkflowService
{
    private async Task CompleteAsync(ActiveRun active, string status, JsonNode? result, string? error, bool notify)
    {
        await active.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (active.State.Status != DynamicWorkflowStatuses.Running) return;
            active.NotificationOwed ??= notify && active.Latch.TryClaimNotification();
            var completed = active.State with
            {
                Status = status,
                Result = result?.DeepClone(),
                Error = error,
                CompletedAt = DateTimeOffset.UtcNow,
                NotificationStatus = active.NotificationOwed.Value ? "pending" : "notApplicable"
            };
            await store.WriteStateAsync(completed, CancellationToken.None).ConfigureAwait(false);
            active.State = completed;
            try
            {
                await store.AppendJournalAsync(completed.RunId, $"run.{status}",
                    error == null ? null : new JsonObject { ["error"] = error }, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to journal committed workflow outcome for {RunId}.", completed.RunId);
            }
            PublishChanged(completed, "state");
        }
        finally { active.Gate.Release(); }
    }

    internal static BackgroundTaskEnding BuildTaskEnding(DynamicWorkflowRun run, string runDirectory, BackgroundTaskLatch? latch)
    {
        var status = run.Status switch
        {
            DynamicWorkflowStatuses.Succeeded => BackgroundTaskStatus.Completed,
            DynamicWorkflowStatuses.Stopped => BackgroundTaskStatus.Stopped,
            _ => BackgroundTaskStatus.Failed
        };
        var stoppedByUser = BackgroundTaskLatch.IsUserStop(status, latch);
        return new BackgroundTaskEnding
        {
            TaskId = run.RunId,
            Kind = BackgroundTaskKinds.Workflow,
            OwnerThreadId = run.ParentThreadId,
            Label = run.Name,
            Status = status,
            KindStatus = run.Status,
            Summary = run.Status switch
            {
                DynamicWorkflowStatuses.Succeeded => $"Workflow \"{run.Name}\" succeeded",
                DynamicWorkflowStatuses.Stopped => stoppedByUser ? $"Workflow \"{run.Name}\" was stopped by the user" : $"Workflow \"{run.Name}\" was stopped",
                DynamicWorkflowStatuses.Failed => $"Workflow \"{run.Name}\" failed",
                _ => $"Workflow \"{run.Name}\" ended with status {run.Status}"
            },
            OutputPath = runDirectory,
            Result = run.Status switch
            {
                DynamicWorkflowStatuses.Succeeded => run.Result?.ToJsonString(),
                DynamicWorkflowStatuses.Stopped => null,
                _ => run.Error
            },
            StoppedByUser = stoppedByUser
        };
    }

    private Task UpdateUsageAsync(ActiveRun active, long inputTokens, long outputTokens, CancellationToken cancellationToken) =>
        UpdateStateAsync(active, state => state with
        {
            InputTokens = state.InputTokens + inputTokens,
            OutputTokens = state.OutputTokens + outputTokens
        }, cancellationToken);

    private async Task UpdateStateAsync(
        ActiveRun active,
        Func<DynamicWorkflowRun, DynamicWorkflowRun> update,
        CancellationToken cancellationToken)
    {
        await active.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (active.State.Status != DynamicWorkflowStatuses.Running) return;
            var next = update(active.State);
            await store.WriteStateAsync(next, cancellationToken).ConfigureAwait(false);
            active.State = next;
            PublishChanged(next, "state");
        }
        finally { active.Gate.Release(); }
    }

    private async Task JournalAsync(ActiveRun active, string type, JsonNode? payload, CancellationToken cancellationToken)
    {
        await active.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (active.State.Status != DynamicWorkflowStatuses.Running) return;
            await store.AppendJournalAsync(active.State.RunId, type, payload, cancellationToken).ConfigureAwait(false);
            if (type is "workflow.phase" or "agent.requested" or "agent.started" or "agent.completed" or "agent.failed" or "agent.replayed")
                PublishChanged(active.State, "progress");
        }
        finally { active.Gate.Release(); }
    }
}
