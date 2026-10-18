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
        return run == null || !string.Equals(run.ParentThreadId, caller.Thread.Id, StringComparison.Ordinal)
            ? null
            : ToTask(run);
    }

    public async Task<IReadOnlyList<BackgroundTask>> ListAsync(BackgroundTaskCaller caller, CancellationToken ct)
    {
        var tasks = new List<BackgroundTask>();
        foreach (var runId in store.EnumerateRunIds().ToArray())
        {
            var run = await service.GetRunAsync(runId, ct).ConfigureAwait(false);
            if (run != null && string.Equals(run.ParentThreadId, caller.Thread.Id, StringComparison.Ordinal))
                tasks.Add(ToTask(run));
        }
        return tasks;
    }

    private BackgroundTask ToTask(DynamicWorkflowRun run) =>
        new BackgroundTask
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
            StartedAt = run.StartedAt ?? run.CreatedAt,
            EndedAt = run.CompletedAt,
            Latch = service.GetStopLatch(run.RunId)
        };

    public Task StopAsync(BackgroundTask task, CancellationToken ct) =>
        task.KindStatus is DynamicWorkflowStatuses.Running or DynamicWorkflowStatuses.Paused
            ? service.StopRunForModelAsync(task.TaskId, ct)
            : Task.CompletedTask;
}
