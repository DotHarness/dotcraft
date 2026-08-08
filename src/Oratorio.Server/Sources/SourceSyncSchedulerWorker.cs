namespace Oratorio.Server.Sources;

/// <summary>
/// Polls provider sync schedules and asks the scheduler service to enqueue due incremental work.
/// </summary>
public sealed class SourceSyncSchedulerWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<SourceSyncSchedulerWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scheduler = scope.ServiceProvider.GetRequiredService<SourceSyncSchedulerService>();
                await scheduler.ProcessDueSchedulesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Source sync scheduler tick failed.");
            }
        }
    }
}
