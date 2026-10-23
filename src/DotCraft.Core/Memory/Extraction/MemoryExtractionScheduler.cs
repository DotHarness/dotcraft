using System.Threading.Channels;
using DotCraft.Contributions;
using DotCraft.Sessions;
using DotCraft.Tools;
using Microsoft.Extensions.Logging;

namespace DotCraft.Memory.Extraction;

/// <summary>Decides after each completed Turn whether memory extraction runs, keeping each Thread's cursor and cadence in process.</summary>
internal sealed class MemoryExtractionScheduler : ITurnLifecycleContributor, IThreadLifecycleContributor, IDisposable
{
    internal const int Cadence = 7;

    private readonly Func<string, CancellationToken, Task<SessionThread>> _loadThread;
    private readonly Func<SessionThread, CancellationToken, IAsyncEnumerable<SessionTurn>> _readTurnsNewestFirst;
    private readonly IMemoryExtractionRunner _runner;
    private readonly ILogger? _logger;
    private readonly Channel<Signal> _signals = Channel.CreateUnbounded<Signal>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<string, ThreadState> _threads = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _idleLock = new();
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _pendingWork;

    public MemoryExtractionScheduler(
        Func<string, CancellationToken, Task<SessionThread>> loadThread,
        Func<SessionThread, CancellationToken, IAsyncEnumerable<SessionTurn>> readTurnsNewestFirst,
        IMemoryExtractionRunner runner,
        ILogger? logger = null)
    {
        _loadThread = loadThread;
        _readTurnsNewestFirst = readTurnsNewestFirst;
        _runner = runner;
        _logger = logger;
        _idle.SetResult();
        _ = Task.Run(ProcessAsync);
    }

    public Task OnTurnEndedAsync(TurnLifecycleContext context, CancellationToken cancellationToken = default)
    {
        if (context.Status == TurnStatus.Completed)
            Post(new TurnCompleted(context.ThreadId, context.TurnId));
        return Task.CompletedTask;
    }

    public Task OnThreadDeletingAsync(SessionThread thread, CancellationToken cancellationToken = default)
    {
        Post(new ThreadDeleted(thread.Id));
        return Task.CompletedTask;
    }

    /// <summary>Completes once every posted Turn is evaluated and no extraction is running.</summary>
    internal Task WhenIdleAsync()
    {
        lock (_idleLock)
            return _idle.Task;
    }

    public void Dispose()
    {
        if (!_signals.Writer.TryComplete())
            return;
        _lifetime.Cancel();
    }

    private void Post(Signal signal)
    {
        BeginWork();
        if (!_signals.Writer.TryWrite(signal))
            EndWork();
    }

    private async Task ProcessAsync()
    {
        await foreach (var signal in _signals.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                switch (signal)
                {
                    case TurnCompleted completed:
                        await EvaluateAsync(completed.ThreadId, completed.TurnId).ConfigureAwait(false);
                        break;
                    case RunFinished finished:
                        Finish(finished);
                        break;
                    case ThreadDeleted deleted:
                        _threads.Remove(deleted.ThreadId);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Memory extraction scheduling failed for thread {ThreadId}.", signal.ThreadId);
            }
            finally
            {
                EndWork();
            }
        }
    }

    private async Task EvaluateAsync(string threadId, string turnId)
    {
        var thread = await _loadThread(threadId, _lifetime.Token).ConfigureAwait(false);
        if (!IsEligible(thread))
            return;

        var hasState = _threads.TryGetValue(threadId, out var state);
        var window = await ReadWindowAsync(thread, turnId, hasState, state?.Cursor).ConfigureAwait(false);
        if (window is null || window.CursorIsNewer)
            return;

        IEnumerable<SessionTurn> range = window.Turns;
        if (state is null)
        {
            _threads[threadId] = state = new ThreadState { Cursor = window.Previous };
            range = window.Turns.Take(1);
        }
        else if (state.Cursor is not null && !window.CursorFound)
        {
            state.Cursor = window.Previous;
            range = window.Turns.Take(1);
        }

        if (range.Any(turn => MemoryUpdates.SucceededPaths(turn).Count > 0))
        {
            state.Cursor = turnId;
            return;
        }

        if (++state.Count < Cadence)
            return;
        state.Count = 0;
        if (state.Running)
            state.Waiting = true;
        else
            Start(threadId, state);
    }

    private async Task<TurnWindow?> ReadWindowAsync(SessionThread thread, string turnId, bool hasState, string? cursor)
    {
        var turns = new List<SessionTurn>();
        string? previous = null;
        await foreach (var turn in _readTurnsNewestFirst(thread, _lifetime.Token).ConfigureAwait(false))
        {
            if (turns.Count == 0)
            {
                if (cursor is not null && turn.Id == cursor)
                    return new TurnWindow(turns, null, CursorFound: true, CursorIsNewer: true);
                if (turn.Id == turnId)
                    turns.Add(turn);
                continue;
            }

            previous ??= turn.Id;
            if (!hasState)
                break;
            if (cursor is not null && turn.Id == cursor)
                return new TurnWindow(turns, previous, CursorFound: true, CursorIsNewer: false);
            turns.Add(turn);
        }

        return turns.Count == 0
            ? null
            : new TurnWindow(turns, previous, CursorFound: cursor is null, CursorIsNewer: false);
    }

    private void Finish(RunFinished finished)
    {
        if (!_threads.TryGetValue(finished.ThreadId, out var state))
            return;
        if (finished.Through is not null && state.Cursor == finished.StartCursor)
            state.Cursor = finished.Through;
        state.Running = false;
        if (state.Waiting)
        {
            state.Waiting = false;
            Start(finished.ThreadId, state);
        }
    }

    private void Start(string threadId, ThreadState state)
    {
        if (_lifetime.IsCancellationRequested)
            return;

        state.Running = true;
        var startCursor = state.Cursor;
        BeginWork();
        _ = Task.Run(async () =>
        {
            string? through = null;
            try
            {
                through = await _runner.RunAsync(threadId, startCursor, _lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Memory extraction failed for thread {ThreadId}.", threadId);
            }
            finally
            {
                Post(new RunFinished(threadId, startCursor, through));
                EndWork();
            }
        });
    }

    private static bool IsEligible(SessionThread thread) =>
        thread.Configuration is { MemoryAutoExtract: true, MemoryEnabled: not false }
        && !ThreadVisibility.IsHidden(thread)
        && ToolPlanningThreadClassifier.Classify(thread) != ToolPlanningThreadKind.SubAgentChild;

    private void BeginWork()
    {
        lock (_idleLock)
        {
            if (_pendingWork++ == 0)
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void EndWork()
    {
        lock (_idleLock)
        {
            if (--_pendingWork == 0)
                _idle.TrySetResult();
        }
    }

    private sealed record TurnWindow(List<SessionTurn> Turns, string? Previous, bool CursorFound, bool CursorIsNewer);

    private sealed class ThreadState
    {
        public string? Cursor { get; set; }

        public int Count { get; set; }

        public bool Running { get; set; }

        public bool Waiting { get; set; }
    }

    private abstract record Signal(string ThreadId);

    private sealed record TurnCompleted(string ThreadId, string TurnId) : Signal(ThreadId);

    private sealed record RunFinished(string ThreadId, string? StartCursor, string? Through) : Signal(ThreadId);

    private sealed record ThreadDeleted(string ThreadId) : Signal(ThreadId);
}
