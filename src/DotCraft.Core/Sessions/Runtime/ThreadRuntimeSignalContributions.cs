using System.Threading.Channels;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

/// <summary>Names one runtime signal to a contributor.</summary>
public sealed record ThreadRuntimeSignalContext(string ThreadId, SessionThreadRuntimeSignal Signal);

/// <summary>Observes thread runtime signals asynchronously after they have been published.</summary>
public interface IThreadRuntimeSignalContributor : IContributionContract
{
    /// <summary>Receives one raised signal. Cancelled when the dispatcher is torn down.</summary>
    Task OnThreadRuntimeSignalAsync(
        ThreadRuntimeSignalContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>Fans signals out through a bounded queue so contributors cannot stall a turn.</summary>
internal sealed class ThreadRuntimeSignalDispatcher : IDisposable
{
    /// <summary>The number of signals buffered before the oldest undelivered one is dropped.</summary>
    public const int QueueCapacity = 256;

    private readonly IContributionView _contributions;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<ThreadRuntimeSignalContext> _queue =
        Channel.CreateBounded<ThreadRuntimeSignalContext>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            });

    private int _dispatchInFlight;
    private int _disposed;

    /// <summary>Creates a dispatcher over a contribution view and starts its pump.</summary>
    public ThreadRuntimeSignalDispatcher(IContributionView contributions, ILoggerFactory? loggerFactory = null)
    {
        _contributions = contributions ?? throw new ArgumentNullException(nameof(contributions));
        _logger = loggerFactory?.CreateLogger<ThreadRuntimeSignalDispatcher>();
        _ = Task.Run(PumpAsync);
    }

    /// <summary>Hands one signal to the queue. Never blocks, never throws, and no-ops while the contribution point is empty.</summary>
    public void Publish(string threadId, SessionThreadRuntimeSignal signal)
    {
        if (string.IsNullOrEmpty(threadId) || Volatile.Read(ref _disposed) != 0)
            return;
        if (_contributions.GetRevision<IThreadRuntimeSignalContributor>() == 0)
            return;

        _queue.Writer.TryWrite(new ThreadRuntimeSignalContext(threadId, signal));
    }

    /// <summary>Blocks until the queue is drained and no contributor call is in flight. Returns false on timeout.</summary>
    public bool WaitForPendingSignals(TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        var spin = new SpinWait();
        while (_queue.Reader.Count > 0 || Volatile.Read(ref _dispatchInFlight) != 0)
        {
            if (Environment.TickCount64 > deadline)
                return false;
            spin.SpinOnce();
        }

        return true;
    }

    /// <summary>Closes the queue and cancels in-flight contributor work. Idempotent, and never waits on a contributor.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Writer.TryComplete();
        try { _stopping.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private async Task PumpAsync()
    {
        try
        {
            while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                // Set before the first read so a drained queue with a call in flight still reads as busy.
                Interlocked.Exchange(ref _dispatchInFlight, 1);
                try
                {
                    while (_queue.Reader.TryRead(out var context))
                        await DispatchAsync(context).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _dispatchInFlight, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Thread runtime signal pump stopped; no further signals reach the IThreadRuntimeSignalContributor contribution point.");
        }
        finally
        {
            _stopping.Dispose();
        }
    }

    private ValueTask DispatchAsync(ThreadRuntimeSignalContext context) =>
        ContributionRead.FanoutAsync(
            _contributions.Resolve<IThreadRuntimeSignalContributor>(context.ThreadId),
            (contributor, token) => new ValueTask(contributor.OnThreadRuntimeSignalAsync(context, token)),
            (contributor, ex) => _logger?.LogWarning(
                ex,
                "Thread runtime signal contributor {ContributorType} failed on {Signal} for thread {ThreadId} and was skipped.",
                contributor.GetType().FullName,
                context.Signal,
                context.ThreadId),
            _stopping.Token);
}
