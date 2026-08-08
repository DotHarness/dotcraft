namespace Oratorio.Server.Services;

public sealed class DiscussionTurnWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<DiscussionTurnWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<DiscussionTurnService>();
                var processed = await service.ProcessPendingAsync(stoppingToken);
                var delay = processed > 0 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromMilliseconds(250);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Agent Discussion Turn worker loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
