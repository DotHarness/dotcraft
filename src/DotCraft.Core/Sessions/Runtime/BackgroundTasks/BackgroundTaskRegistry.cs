namespace DotCraft.Sessions;

public sealed class BackgroundTaskRegistry
{
    internal const string ModelStopOrigin = "model";
    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, IBackgroundTaskKind> _kinds = new(StringComparer.Ordinal);

    public BackgroundTaskRegistry(IEnumerable<IBackgroundTaskKind> kinds)
    {
        foreach (var kind in kinds)
            _kinds[kind.Kind] = kind;
    }

    public async Task<BackgroundTask> FindAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct = default)
    {
        var id = taskId?.Trim();
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("taskId is required.", nameof(taskId));
        var task = _kinds.TryGetValue(BackgroundTaskKinds.Of(id), out var kind)
            ? await kind.FindAsync(caller, id, ct).ConfigureAwait(false)
            : null;
        return task ?? throw new KeyNotFoundException($"'{id}' matches no background task you own.");
    }

    /// <summary>
    /// Stops a task and waits a bounded time for it to settle. A task that settles is reported with its
    /// final status and owes no notification; one that does not is reported as <c>stopping</c> and notifies when it ends.
    /// </summary>
    public async Task<BackgroundTask> StopAsync(BackgroundTaskCaller caller, string taskId, CancellationToken ct = default)
    {
        var task = await FindAsync(caller, taskId, ct).ConfigureAwait(false);
        var kind = _kinds[task.Kind];
        var latch = task.Latch;
        latch?.BeginStop(ModelStopOrigin);
        try
        {
            await kind.StopAsync(task, ct).WaitAsync(StopWait, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
        }
        catch
        {
            latch?.EndStop(settled: false);
            throw;
        }

        var current = await ReadAsync(kind, caller, task).ConfigureAwait(false);
        var settled = current.Status != BackgroundTaskStatus.Running;
        if (latch != null && latch.EndStop(settled) && !settled)
        {
            current = await ReadAsync(kind, caller, task).ConfigureAwait(false);
            settled = true;
        }
        return settled ? current : current with { Status = BackgroundTaskStatus.Stopping };
    }

    private static async Task<BackgroundTask> ReadAsync(IBackgroundTaskKind kind, BackgroundTaskCaller caller, BackgroundTask task) =>
        await kind.FindAsync(caller, task.TaskId, CancellationToken.None).ConfigureAwait(false) ?? task;
}
