namespace DotCraft.Oratorio.GitHub;

public sealed class GitHubCommentScanWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<GitHubCommentScanWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scanner = scope.ServiceProvider.GetRequiredService<GitHubCommentScanService>();
                await scanner.ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GitHub comment scan tick failed.");
            }
        }
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }
}
