using DotCraft.RemoteTools;

namespace DotCraft.Satellite.Services;

/// <summary>
/// The runtime binds its connections when it starts, so accepting an invitation restarts it.
/// </summary>
internal sealed class SatelliteRuntimeConnection : IAsyncDisposable
{
    private readonly RemoteToolHostRuntime _runtime;
    private bool _running;

    public SatelliteRuntimeConnection(RemoteToolHostRuntime runtime) => _runtime = runtime;

    public RemoteToolHostRuntime Runtime => _runtime;

    public bool PauseRequested { get; private set; }

    public bool HasPairing => _runtime.Peers.Count > 0;

    public void Start()
    {
        if (_running || !HasPairing)
            return;
        _running = true;
        // RunAsync completes only when the host stops, so its task is deliberately not awaited.
        _ = _runtime.RunAsync();
    }

    public async Task RestartAsync()
    {
        if (_running)
        {
            _running = false;
            await _runtime.StopAsync().ConfigureAwait(false);
        }
        Start();
        if (PauseRequested && _running)
            await _runtime.SetSharingPausedAsync(paused: true).ConfigureAwait(false);
    }

    public async Task SetPausedAsync(bool paused)
    {
        PauseRequested = paused;
        await _runtime.SetSharingPausedAsync(paused).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _running = false;
        await _runtime.DisposeAsync().ConfigureAwait(false);
    }
}
