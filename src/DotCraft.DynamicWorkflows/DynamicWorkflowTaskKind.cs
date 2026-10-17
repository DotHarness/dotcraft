using DotCraft.Sessions;

namespace DotCraft.DynamicWorkflows;

internal sealed class DynamicWorkflowTaskKind(DynamicWorkflowService service, DynamicWorkflowStore store) : IBackgroundTaskKind
{
    public string Kind => BackgroundTaskKinds.Workflow;

    public async Task<BackgroundTask?> FindAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct)
    {
        DynamicWorkflowRun? run;
        try
        {
            run = await service.GetRunAsync(taskId, ct).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (run == null || !string.Equals(run.ParentThreadId, caller.Thread.Id, StringComparison.Ordinal))
            return null;

        return new BackgroundTask
        {
            TaskId = run.RunId,
            Kind = Kind,
            OwnerThreadId = run.ParentThreadId,
            TurnId = run.ParentTurnId,
            Label = run.Name,
            Status = run.Status switch
            {
                DynamicWorkflowStatuses.Running => BackgroundTaskStatus.Running,
                DynamicWorkflowStatuses.Succeeded => BackgroundTaskStatus.Completed,
                DynamicWorkflowStatuses.Failed or DynamicWorkflowStatuses.Interrupted => BackgroundTaskStatus.Failed,
                _ => BackgroundTaskStatus.Stopped
            },
            KindStatus = run.Status,
            OutputPath = store.GetRunDirectory(run.RunId),
            Latch = service.GetStopLatch(run.RunId)
        };
    }

    public Task StopAsync(BackgroundTask task, CancellationToken ct) =>
        task.KindStatus is DynamicWorkflowStatuses.Running or DynamicWorkflowStatuses.Paused
            ? service.StopRunForModelAsync(task.TaskId, ct)
            : Task.CompletedTask;
}
