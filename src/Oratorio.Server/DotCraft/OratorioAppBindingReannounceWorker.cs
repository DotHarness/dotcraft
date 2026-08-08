using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Oratorio.Server.DotCraft;

/// <summary>
/// Rebinds saved MCP authorities after restart and periodically republishes the app-owned board surface.
/// </summary>
public sealed class OratorioAppBindingReannounceWorker(
    IHostApplicationLifetime lifetime,
    IServer server,
    OratorioAppBindingService appBindingService,
    ILogger<OratorioAppBindingReannounceWorker> logger) : BackgroundService
{
    private const int SurfacePublishIntervalSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken);

        var baseUrl = ResolveLiveBaseUrl();
        if (baseUrl is null)
        {
            logger.LogDebug("Skipping DotCraft re-announce: no live loopback surface URL resolved.");
            return;
        }

        await TryRebindAsync(baseUrl, stoppingToken);
        await TryPublishSurfaceAsync(baseUrl, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SurfacePublishIntervalSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await TryPublishSurfaceAsync(baseUrl, stoppingToken);
        }
    }

    private async Task TryRebindAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            await appBindingService.RebindPersistedAsync(baseUrl, ct);
            logger.LogInformation("Rebound persisted Oratorio MCP bindings at {BaseUrl}.", baseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Skipped DotCraft MCP rebind (app-server may be unavailable).");
        }
    }

    private async Task TryPublishSurfaceAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            await appBindingService.PublishSurfacePersistedAsync(baseUrl, ct);
            logger.LogInformation("Published the Oratorio board surface at {BaseUrl}.", baseUrl);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Skipped DotCraft board surface publish (app-server may be unavailable); retrying later.");
        }
    }

    private string? ResolveLiveBaseUrl()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is null)
        {
            return null;
        }

        foreach (var address in addresses)
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttp &&
                uri.IsLoopback)
            {
                return uri.GetLeftPart(UriPartial.Authority);
            }
        }

        return null;
    }
}
