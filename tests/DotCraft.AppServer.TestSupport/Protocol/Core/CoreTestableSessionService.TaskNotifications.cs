using DotCraft.Sessions;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

internal sealed partial class CoreTestableSessionService : IBackgroundTaskNotifications
{
    private readonly Lock _taskEndingsLock = new();
    private readonly List<BackgroundTaskEnding> _taskEndings = [];

    public IReadOnlyList<BackgroundTaskEnding> TaskEndings
    {
        get
        {
            lock (_taskEndingsLock)
                return _taskEndings.ToArray();
        }
    }

    public Task<string?> NotifyBackgroundTaskEndedAsync(BackgroundTaskEnding ending, CancellationToken ct = default)
    {
        lock (_taskEndingsLock)
            _taskEndings.Add(ending);
        return Task.FromResult<string?>($"queued_{_taskEndings.Count}");
    }

    public async Task<BackgroundTaskEnding> WaitForTaskEndingAsync(string taskId, int occurrence = 1)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var match = TaskEndings.Where(ending => ending.TaskId == taskId).Skip(occurrence - 1).FirstOrDefault();
            if (match != null)
                return match;
            await Task.Delay(25);
        }
        throw new TimeoutException($"No notification for task '{taskId}'.");
    }
}
