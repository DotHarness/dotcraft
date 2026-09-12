namespace DotCraft.RemoteTools;

internal sealed class RemoteOperationScope : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _calls;
    private Task? _closing;

    internal bool Closed { get { lock (_gate) return _closing is not null; } }

    internal Call Enter(CancellationToken ct) => TryEnter(ct) ?? throw new ObjectDisposedException(nameof(RemoteOperationScope));

    internal Call? TryEnter(CancellationToken ct)
    {
        lock (_gate)
        {
            if (_closing is not null) return null;
            ct.ThrowIfCancellationRequested();
            _calls++;
            return new(CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping.Token), Leave);
        }
    }

    private void Leave()
    {
        lock (_gate)
            if (--_calls == 0 && _closing is not null) _drained.TrySetResult();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_closing ??= CloseAsync());
    }

    private async Task CloseAsync()
    {
        await Task.Yield();
        var cancellation = _stopping.CancelAsync();
        lock (_gate)
            if (_calls == 0) _drained.TrySetResult();
        await Task.WhenAll(cancellation, _drained.Task).ConfigureAwait(false);
        _stopping.Dispose();
    }

    internal sealed class Call(CancellationTokenSource cancellation, Action leave) : IDisposable
    {
        private Action? _leave = leave;
        internal CancellationToken Token => cancellation.Token;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _leave, null) is not { } release) return;
            cancellation.Dispose();
            release();
        }
    }
}
