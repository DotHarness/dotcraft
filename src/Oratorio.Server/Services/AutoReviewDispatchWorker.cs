namespace Oratorio.Server.Services;

public sealed class AutoReviewDispatchWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<AutoReviewDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<AutoReviewDispatchService>();
                await dispatcher.DispatchEligibleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Auto Review dispatch tick failed.");
            }
        }
    }
}
