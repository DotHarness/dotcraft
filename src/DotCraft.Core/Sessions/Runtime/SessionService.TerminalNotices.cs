using System.Collections.Concurrent;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private readonly ConcurrentDictionary<string, ForegroundHandOverSignal> _foregroundHandOver = new(StringComparer.Ordinal);
    private readonly object _terminalNoticeSync = new();
    private Task _terminalNoticeChain = Task.CompletedTask;
    private int _terminalNoticesAttached;

    internal void AttachTerminalNotices()
    {
        if (BackgroundTerminalService is not { } terminals || Interlocked.Exchange(ref _terminalNoticesAttached, 1) != 0)
            return;
        terminals.TerminalEvent += OnTerminalNoticeEvent;
    }

    internal Task DrainTerminalNoticesAsync()
    {
        lock (_terminalNoticeSync)
            return _terminalNoticeChain;
    }

    private void OnTerminalNoticeEvent(BackgroundTerminalEvent evt)
    {
        if (evt.EventType is not (BackgroundTerminalEventTypes.CompletionNotice or BackgroundTerminalEventTypes.StallNotice)
            || string.IsNullOrWhiteSpace(evt.Terminal.TurnId))
        {
            return;
        }

        BackgroundTaskLatch? latch = null;
        if (evt.EventType == BackgroundTerminalEventTypes.CompletionNotice
            && _terminalStopLatches.TryRemove(evt.Terminal.SessionId, out latch)
            && !latch.TryClaimNotification())
        {
            return;
        }

        var ending = ToTaskEnding(evt.Terminal, latch);
        lock (_terminalNoticeSync)
            _terminalNoticeChain = _terminalNoticeChain.ContinueWith(
                _ => DeliverTerminalNoticeAsync(ending),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
    }

    private async Task DeliverTerminalNoticeAsync(BackgroundTaskEnding ending)
    {
        try
        {
            await NotifyBackgroundTaskEndedAsync(ending, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to deliver notification for terminal {SessionId} to thread {ThreadId}",
                ending.TaskId, ending.OwnerThreadId);
        }
    }

    private static BackgroundTaskEnding ToTaskEnding(BackgroundTerminalSnapshot terminal, BackgroundTaskLatch? latch)
    {
        var task = BackgroundTaskTools.ToTask(terminal);
        var stalled = terminal.WaitingForInput;
        var stoppedByUser = !stalled && BackgroundTaskLatch.IsUserStop(task.Status, latch);
        return new BackgroundTaskEnding
        {
            TaskId = terminal.SessionId,
            Kind = BackgroundTaskKinds.Shell,
            OwnerThreadId = terminal.ThreadId,
            StartTurnId = terminal.TurnId,
            Label = task.Label,
            Status = stalled ? BackgroundTaskStatus.WaitingForInput : task.Status,
            KindStatus = stalled ? BackgroundTaskStatus.WaitingForInput : terminal.Status,
            Summary = BuildTerminalSummary(terminal, task.Label, stoppedByUser),
            ExitCode = stalled ? null : terminal.ExitCode,
            OutputPath = terminal.OutputPath,
            Result = stalled && !string.IsNullOrEmpty(terminal.Output) ? terminal.Output : null,
            StoppedByUser = stoppedByUser,
            StdinOpen = terminal.Interactive
        };
    }

    private static string BuildTerminalSummary(BackgroundTerminalSnapshot terminal, string command, bool stoppedByUser)
    {
        var prefix = $"Background command \"{command}\"";
        if (terminal.WaitingForInput)
            return $"{prefix} appears to be waiting for interactive input";
        return terminal.Status switch
        {
            BackgroundTerminalStatus.Completed when terminal.ExitCode is { } exitCode => $"{prefix} completed (exit code {exitCode})",
            BackgroundTerminalStatus.Failed when terminal.ExitCode is { } exitCode => $"{prefix} failed with exit code {exitCode}",
            BackgroundTerminalStatus.Killed => stoppedByUser ? $"{prefix} was stopped by the user" : $"{prefix} was stopped",
            BackgroundTerminalStatus.TimedOut => $"{prefix} timed out",
            BackgroundTerminalStatus.Lost => $"{prefix} was lost",
            _ => $"{prefix} ended with status {terminal.Status}"
        };
    }

    private IDisposable RegisterForegroundHandOver(string turnId)
    {
        var signal = new ForegroundHandOverSignal();
        _foregroundHandOver[turnId] = signal;
        return new ForegroundHandOverRegistration(this, turnId, signal);
    }

    private CancellationToken GetForegroundHandOverToken(string turnId) =>
        _foregroundHandOver.TryGetValue(turnId, out var signal) ? signal.Token : CancellationToken.None;

    private void SignalForegroundHandOver(string turnId)
    {
        if (_foregroundHandOver.TryGetValue(turnId, out var signal))
            signal.Signal();
    }

    private void ResetForegroundHandOver(string turnId)
    {
        if (_foregroundHandOver.TryGetValue(turnId, out var signal))
            signal.Reset();
    }

    private sealed class ForegroundHandOverSignal
    {
        private readonly object _sync = new();
        private CancellationTokenSource _source = new();

        public CancellationToken Token
        {
            get
            {
                lock (_sync)
                    return _source.Token;
            }
        }

        public void Signal()
        {
            CancellationTokenSource source;
            lock (_sync)
                source = _source;
            source.Cancel();
        }

        public void Reset()
        {
            lock (_sync)
            {
                if (_source.IsCancellationRequested)
                    _source = new CancellationTokenSource();
            }
        }
    }

    private sealed class ForegroundHandOverRegistration(SessionService owner, string turnId, ForegroundHandOverSignal signal) : IDisposable
    {
        public void Dispose() =>
            owner._foregroundHandOver.TryRemove(new KeyValuePair<string, ForegroundHandOverSignal>(turnId, signal));
    }
}
