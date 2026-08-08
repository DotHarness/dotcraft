namespace Oratorio.Server.GitHub;

public sealed class GitHubSyncWorker(GitHubSyncCoordinator coordinator, ILogger<GitHubSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await coordinator.RecoverInterruptedJobsAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await coordinator.ProcessNextQueuedJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GitHub sync worker tick failed.");
            }
        }
    }
}
