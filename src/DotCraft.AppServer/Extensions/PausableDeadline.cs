using System.Diagnostics;

namespace DotCraft.AppServer;

internal sealed class PausableDeadline : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Lock _gate = new();
    private TimeSpan _remaining;
    private long _startedAt;
    private int _pauses;
    private bool _disposed;

    public PausableDeadline(TimeSpan duration, CancellationToken linkedToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken);
        _remaining = duration;
        _startedAt = Stopwatch.GetTimestamp();
        _cts.CancelAfter(duration);
    }

    public CancellationToken Token => _cts.Token;

    public void Pause()
    {
        lock (_gate)
        {
            if (_disposed || _pauses++ > 0)
                return;
            _remaining -= Stopwatch.GetElapsedTime(_startedAt);
            if (_remaining < TimeSpan.Zero)
                _remaining = TimeSpan.Zero;
            _cts.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    public void Resume()
    {
        lock (_gate)
        {
            if (_disposed || _pauses == 0 || --_pauses > 0)
                return;
            _startedAt = Stopwatch.GetTimestamp();
            _cts.CancelAfter(_remaining);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _cts.Dispose();
        }
    }
}
