using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Configuration;
using Microsoft.Extensions.Logging;

namespace DotCraft.Harness;

public sealed class RemoteModelRuntimeConnection(
    AppConfig config,
    RemoteProviderTransport transport,
    IAppConfigMonitor monitor,
    ILogger<RemoteModelRuntimeConnection> logger) : IProviderConnectionLifecycle, IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _refreshLoop;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_refreshLoop is not null)
            return;
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        _refreshLoop = RefreshLoopAsync();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var catalog = await transport.GetCatalogAsync(timeout.Token).ConfigureAwait(false);
            RemoteModelConfiguration.Apply(config, catalog);
            monitor.NotifyChanged("model-service", ["Providers"]);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Model service catalog is unavailable: {Message}", ex.Message);
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await RefreshAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_refreshLoop is not null)
            await _refreshLoop.ConfigureAwait(false);
        _stop.Dispose();
    }
}
