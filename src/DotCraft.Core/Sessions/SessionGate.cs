using System.Collections.Concurrent;

namespace DotCraft.Sessions;

/// <summary>
/// Provides per-session mutual exclusion so that concurrent requests targeting the same
/// sessionId are serialized, while different sessions remain fully parallel.
/// Supports a configurable max pending queue size per session -- when exceeded the oldest
/// waiting request is evicted with <see cref="SessionGateOverflowException"/>.
/// </summary>
public sealed class SessionGate
{
    private readonly ConcurrentDictionary<string, SessionQueue> _queues = new(StringComparer.Ordinal);
    private readonly int _maxQueueSize;

    /// <param name="maxQueueSize">
    /// Maximum number of pending (waiting) requests per session.
    /// 0 means unlimited (no eviction).
    /// </param>
    public SessionGate(int maxQueueSize = 0)
    {
        _maxQueueSize = maxQueueSize;
    }

    /// <summary>
    /// Acquires exclusive access for the given session. Dispose the returned handle to release.
    /// If the session's pending queue is full, the oldest waiting request is evicted.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string sessionId, CancellationToken ct = default)
    {
        var queue = _queues.GetOrAdd(sessionId, _ => new SessionQueue());
        return await queue.EnqueueAsync(sessionId, _maxQueueSize, ct);
    }

    private sealed class SessionQueue
    {
        private readonly Lock _lock = new();
        private bool _isActive;
        private readonly LinkedList<Waiter> _waiters = new();

        public async Task<IDisposable> EnqueueAsync(string sessionId, int maxQueueSize, CancellationToken ct)
        {
            Waiter waiter;
            lock (_lock)
            {
                if (!_isActive)
                {
                    _isActive = true;
                    return new Releaser(this);
                }

                // Evict oldest waiters if queue is at capacity
                while (maxQueueSize > 0 && _waiters.Count >= maxQueueSize)
                {
                    var oldest = _waiters.First!.Value;
                    _waiters.RemoveFirst();
                    oldest.Tcs.TrySetException(new SessionGateOverflowException(sessionId));
                }

                waiter = new Waiter();
                _waiters.AddLast(waiter);
            }

            // Register external cancellation so the waiter is removed on token cancel
            await using var reg = ct.Register(() =>
            {
                lock (_lock)
                {
                    if (_waiters.Remove(waiter))
                        waiter.Tcs.TrySetCanceled(ct);
                }
            });

            return await waiter.Tcs.Task;
        }

        private void Release()
        {
            lock (_lock)
            {
                while (_waiters.Count > 0)
                {
                    var next = _waiters.First!.Value;
                    _waiters.RemoveFirst();

                    if (next.Tcs.TrySetResult(new Releaser(this)))
                        return;
                }

                _isActive = false;
            }
        }

        private sealed class Waiter
        {
            public TaskCompletionSource<IDisposable> Tcs { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class Releaser(SessionQueue queue) : IDisposable
        {
            public void Dispose() => queue.Release();
        }
    }
}
