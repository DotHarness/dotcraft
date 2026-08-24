using System.Threading.Channels;
using DotCraft.Contributions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tracing;

/// <summary>
/// Observes trace events after <see cref="TraceStore"/> has accepted them. Observation only: the
/// return is void and the event is already applied, so a sink can neither alter nor suppress one.
/// </summary>
public interface ITraceSink : IContributionContract
{
    /// <summary>Gets the stable, kebab-case sink name used in diagnostics.</summary>
    string Name { get; }

    /// <summary>Receives one recorded trace event, off the thread that recorded it.</summary>
    void Record(TraceEvent evt);
}

/// <summary>Fans recorded trace events out to the <see cref="ITraceSink"/> contribution point off the recording thread.</summary>
/// <remarks>
/// The hand-off is a bounded queue drained by one pump: recording never waits on a sink, and a
/// stalled sink costs dropped events rather than a stalled turn.
/// </remarks>
public sealed class TraceSinkDispatcher : IDisposable
{
    /// <summary>The number of events buffered before the oldest unread one is dropped.</summary>
    public const int QueueCapacity = 1024;

    private readonly IContributionView _contributions;
    private readonly ILogger? _logger;
    private readonly Channel<TraceEvent> _queue = Channel.CreateBounded<TraceEvent>(
        new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private int _dispatchInFlight;
    private int _disposed;

    /// <summary>Creates a dispatcher over a contribution view and starts its pump.</summary>
    public TraceSinkDispatcher(IContributionView contributions, ILoggerFactory? loggerFactory = null)
    {
        _contributions = contributions ?? throw new ArgumentNullException(nameof(contributions));
        _logger = loggerFactory?.CreateLogger<TraceSinkDispatcher>();
        _ = Task.Run(PumpAsync);
    }

    /// <summary>Hands one event to the queue. Never blocks, never throws, and no-ops while the contribution point is empty.</summary>
    public void Publish(TraceEvent evt)
    {
        if (evt is null || Volatile.Read(ref _disposed) != 0)
            return;
        if (_contributions.GetRevision<ITraceSink>() == 0)
            return;

        _queue.Writer.TryWrite(evt);
    }

    /// <summary>Blocks until the queue is drained and no sink call is in flight. Returns false on timeout.</summary>
    public bool WaitForPendingSinks(TimeSpan timeout)
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

    /// <summary>Closes the queue to new events and lets the pump drain and exit. Idempotent.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.Writer.TryComplete();
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
                    while (_queue.Reader.TryRead(out var evt))
                        Dispatch(evt);
                }
                finally
                {
                    Interlocked.Exchange(ref _dispatchInFlight, 0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Trace sink pump stopped; no further events reach the ITraceSink contribution point.");
        }
    }

    private void Dispatch(TraceEvent evt) =>
        ContributionRead.Fanout(
            _contributions.Resolve<ITraceSink>(),
            sink => sink.Record(evt),
            (sink, ex) => _logger?.LogWarning(
                ex,
                "Trace sink '{Sink}' threw for event {EventType} and was skipped.",
                SafeName(sink),
                evt.Type));

    private static string SafeName(ITraceSink sink)
    {
        try
        {
            return sink.Name;
        }
        catch
        {
            return sink.GetType().Name;
        }
    }
}
