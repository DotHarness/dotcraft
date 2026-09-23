namespace DotCraft.SessionImport;

public sealed class SessionImportSyncRuntime(SessionImportService service)
{
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaximumInterval = TimeSpan.FromDays(30);

    private CancellationTokenSource? _lifetime;
    private Task _loop = Task.CompletedTask;

    public TimeSpan StartDelay { get; init; } = TimeSpan.FromSeconds(10);

    public void Start()
    {
        _lifetime = new CancellationTokenSource();
        service.SyncActivated += OnSyncActivated;
        var token = _lifetime.Token;
        _loop = Task.Run(() => LoopAsync(token), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        if (_lifetime is not { } lifetime)
            return;
        service.SyncActivated -= OnSyncActivated;
        _lifetime = null;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await _loop.ConfigureAwait(false);
        await service.StopPassesAsync().ConfigureAwait(false);
        lifetime.Dispose();
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartDelay, ct).ConfigureAwait(false);
            var interval = TimeSpan.FromTicks(Math.Clamp(service.SyncInterval.Ticks, MinimumInterval.Ticks, MaximumInterval.Ticks));
            using var timer = new PeriodicTimer(interval);
            do
            {
                RequestPassIfActive();
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private void OnSyncActivated()
    {
        if (_lifetime is not null)
            RequestPassIfActive();
    }

    private void RequestPassIfActive()
    {
        if (service.IsSyncActive)
            service.RequestSyncPass();
    }
}
