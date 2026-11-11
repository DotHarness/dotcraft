namespace DotCraft.Sessions;

internal sealed class ThreadMcpRefresh : IAsyncDisposable
{
    private readonly Lock _sync = new();
    private readonly CancellationTokenSource _stop = new();
    private Task _task = Task.CompletedTask;
    private Task? _disposeTask;
    private bool _pending;
    private bool _running;
    private bool _closed;

    public void Request(Func<CancellationToken, Task> refresh, Action<Exception> reportFailure)
    {
        lock (_sync)
        {
            if (_closed) return;
            _pending = true;
            if (_running) return;
            _running = true;
            _task = Task.Run(() => RunAsync(refresh, reportFailure));
        }
    }

    public Task WaitAsync(CancellationToken ct)
    {
        lock (_sync)
            return _task.WaitAsync(ct);
    }

    private async Task RunAsync(Func<CancellationToken, Task> refresh, Action<Exception> reportFailure)
    {
        while (true)
        {
            lock (_sync)
            {
                if (_closed || !_pending)
                {
                    _running = false;
                    return;
                }
                _pending = false;
            }
            try
            {
                await refresh(_stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                reportFailure(exception);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            _closed = true;
            return new ValueTask(_disposeTask ??= StopAsync(_task));
        }
    }

    private async Task StopAsync(Task task)
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        await task.ConfigureAwait(false);
        _stop.Dispose();
    }
}
