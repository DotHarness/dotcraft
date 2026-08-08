namespace Oratorio.Server.GitLab;

public sealed class GitLabSyncWorker(GitLabSyncCoordinator coordinator, ILogger<GitLabSyncWorker> logger) : BackgroundService
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
                logger.LogError(ex, "GitLab sync worker tick failed.");
            }
        }
    }
}
