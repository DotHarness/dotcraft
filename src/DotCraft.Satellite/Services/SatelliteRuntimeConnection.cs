using DotCraft.RemoteTools;

namespace DotCraft.Satellite.Services;

/// <summary>
/// The runtime binds its connections when it starts, so accepting an invitation restarts it.
/// </summary>
internal sealed class SatelliteRuntimeConnection : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly RemoteToolHostRuntime _runtime;
    private readonly SatelliteLog _log;
    private Task? _runTask;
    private bool _running;

    public SatelliteRuntimeConnection(RemoteToolHostRuntime runtime, SatelliteLog log)
    {
        _runtime = runtime;
        _log = log;
        _runtime.Diagnostic += OnDiagnostic;
    }

    public RemoteToolHostRuntime Runtime => _runtime;

    public bool PauseRequested { get; private set; }

    public bool HasPairing => _runtime.Peers.Count > 0;

    internal bool IsRunning
    {
        get
        {
            lock (_gate)
                return _running;
        }
    }

    public void Start()
    {
        Task running;
        lock (_gate)
        {
            if (_running || !HasPairing)
                return;
            try
            {
                running = _runtime.RunAsync();
            }
            catch (Exception exception)
            {
                _log.Error("runtime.start.failed", "The remote tool host could not start.", exception);
                return;
            }
            _running = true;
            _runTask = running;
        }

        _log.Information("runtime.started", "The remote tool host started.");
        _ = ObserveAsync(running);
    }

    public async Task RestartAsync()
    {
        if (IsRunning)
        {
            lock (_gate)
                _running = false;
            await _runtime.StopAsync().ConfigureAwait(false);
        }
        Start();
        if (PauseRequested && IsRunning)
            await _runtime.SetSharingPausedAsync(paused: true).ConfigureAwait(false);
    }

    public async Task SetPausedAsync(bool paused)
    {
        PauseRequested = paused;
        await _runtime.SetSharingPausedAsync(paused).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
            _running = false;
        _runtime.Diagnostic -= OnDiagnostic;
        await _runtime.DisposeAsync().ConfigureAwait(false);
        _log.Information("runtime.stopped", "The remote tool host stopped.");
    }

    private async Task ObserveAsync(Task running)
    {
        Exception? failure = null;
        try
        {
            await running.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var unexpected = false;
        lock (_gate)
        {
            if (!ReferenceEquals(_runTask, running))
                return;
            unexpected = _running;
            _running = false;
            _runTask = null;
        }

        if (!unexpected)
            return;
        if (failure is null)
            _log.Warning("runtime.completed", "The remote tool host stopped unexpectedly; the satellite process remains active.");
        else
            _log.Error("runtime.failed", "The remote tool host failed; the satellite process remains active.", failure);
    }

    private void OnDiagnostic(RemoteToolHostDiagnostic diagnostic)
    {
        switch (diagnostic.Level)
        {
            case RemoteToolHostDiagnosticLevel.Information:
                _log.Information(diagnostic.EventName, diagnostic.Message);
                break;
            case RemoteToolHostDiagnosticLevel.Warning:
                _log.Warning(diagnostic.EventName, diagnostic.Message, diagnostic.Exception);
                break;
            case RemoteToolHostDiagnosticLevel.Error:
                _log.Error(diagnostic.EventName, diagnostic.Message, diagnostic.Exception);
                break;
            default:
                break;
        }
    }
}
