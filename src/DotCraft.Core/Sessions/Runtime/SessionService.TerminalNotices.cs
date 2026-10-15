using System.Collections.Concurrent;
using System.Text;
using DotCraft.Sessions.Wire;
using DotCraft.Tools.BackgroundTerminals;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    internal const string TerminalTriggerKind = "terminal";
    private const int TerminalLabelMaxChars = 80;

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

        lock (_terminalNoticeSync)
            _terminalNoticeChain = _terminalNoticeChain.ContinueWith(
                _ => DeliverTerminalNoticeAsync(evt.Terminal),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default).Unwrap();
    }

    private async Task DeliverTerminalNoticeAsync(BackgroundTerminalSnapshot terminal)
    {
        try
        {
            var settings = (_appConfigMonitor?.Current ?? AgentFactory.RuntimeContext.Config).Tools.Shell.Background;
            if (!string.Equals(settings.CompletionDelivery, "wake", StringComparison.OrdinalIgnoreCase))
                return;

            var thread = await GetOrLoadThreadAsync(terminal.ThreadId, CancellationToken.None).ConfigureAwait(false);
            if (thread.Status != ThreadStatus.Active)
                return;

            var queued = await InvokeThreadCommandAsync(
                terminal.ThreadId,
                ct => EnqueueTerminalNoticeAsync(terminal, Math.Max(0, settings.MaxConsecutiveWakes), ct),
                CancellationToken.None).ConfigureAwait(false);
            if (queued)
                await TryStartNextQueuedTurnAsync(terminal.ThreadId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to deliver completion notice for terminal {SessionId} to thread {ThreadId}",
                terminal.SessionId, terminal.ThreadId);
        }
    }

    private async Task<bool> EnqueueTerminalNoticeAsync(BackgroundTerminalSnapshot terminal, int maxConsecutiveWakes, CancellationToken ct)
    {
        var thread = await GetOrLoadThreadAsync(terminal.ThreadId, ct).ConfigureAwait(false);
        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        bool idle;
        using (await AcquireThreadQueueLockAsync(terminal.ThreadId, ct).ConfigureAwait(false))
        {
            if (_runtimeRegistry.TryGetThread(terminal.ThreadId, out var cachedThread))
                thread = cachedThread;
            if (thread.Status != ThreadStatus.Active)
                return false;

            var activeTurn = thread.Turns.LastOrDefault(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
            idle = activeTurn == null;
            if (idle && CountTrailingTerminalWakes(thread) >= maxConsecutiveWakes)
            {
                logger?.LogInformation(
                    "Skipped completion notice for terminal {SessionId}: thread {ThreadId} reached its consecutive wake limit",
                    terminal.SessionId, thread.Id);
                return false;
            }

            var displayText = BuildTerminalNoticeDisplayText(terminal);
            var queue = thread.QueuedInputs.ToList();
            queue.Add(new QueuedTurnInput
            {
                Id = SessionIdGenerator.NewQueuedInputId(),
                ThreadId = thread.Id,
                NativeInputParts = [new SessionInputPart { Type = "text", Text = displayText }],
                MaterializedInputParts = [new SessionInputPart { Type = "text", Text = BuildTerminalNoticeModelText(terminal) }],
                DisplayText = displayText,
                Status = idle ? "queued" : "guidancePending",
                CreatedAt = DateTimeOffset.UtcNow,
                ReadyAfterTurnId = activeTurn?.Id,
                TriggerKind = TerminalTriggerKind,
                TriggerLabel = ShortenTerminalCommand(terminal.Command),
                TriggerRefId = terminal.SessionId
            });
            thread.QueuedInputs = queue;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await PersistThreadWithMaterializationAsync(thread, ct).ConfigureAwait(false);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        return idle;
    }

    private static int CountTrailingTerminalWakes(SessionThread thread)
    {
        var count = 0;
        for (var i = thread.Turns.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(thread.Turns[i].Input?.AsUserMessage?.TriggerKind, TerminalTriggerKind, StringComparison.Ordinal))
                break;
            count++;
        }
        return count;
    }

    private static bool IsTerminalNotice(QueuedTurnInput input) =>
        string.Equals(input.TriggerKind, TerminalTriggerKind, StringComparison.Ordinal);

    private static string ShortenTerminalCommand(string command)
    {
        var singleLine = string.Join(' ', command.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= TerminalLabelMaxChars ? singleLine : singleLine[..(TerminalLabelMaxChars - 3)] + "...";
    }

    private static string BuildTerminalNoticeDisplayText(BackgroundTerminalSnapshot terminal)
    {
        if (terminal.WaitingForInput)
            return $"Background command is waiting for input: {ShortenTerminalCommand(terminal.Command)}";
        var outcome = terminal.ExitCode is { } exitCode ? $"exit code {exitCode}" : terminal.Status;
        return $"Background command finished ({outcome}): {ShortenTerminalCommand(terminal.Command)}";
    }

    private static string BuildTerminalNoticeModelText(BackgroundTerminalSnapshot terminal)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<terminal_notification>");
        if (terminal.WaitingForInput)
            sb.AppendLine($"Background terminal {terminal.SessionId} appears to be waiting for input.");
        else
        {
            sb.Append($"Background terminal {terminal.SessionId} finished with status {terminal.Status}");
            if (terminal.ExitCode is { } exitCode)
                sb.Append($" and exit code {exitCode}");
            sb.AppendLine($" after {FormatWallTime(terminal.WallTimeMs)}.");
        }
        sb.AppendLine($"Command: {ShortenTerminalCommand(terminal.Command)}");
        sb.AppendLine($"Output log: {terminal.OutputPath}");
        if (string.IsNullOrEmpty(terminal.Output))
            sb.AppendLine("No unread output.");
        else
        {
            if (terminal.Truncated || terminal.Lossy)
                sb.AppendLine($"Last {terminal.Output.Length} of {terminal.OriginalOutputChars} unread chars. WriteStdin with empty input returns all of them:");
            else
                sb.AppendLine("Unread output:");
            sb.AppendLine(terminal.Output);
        }
        if (terminal.WaitingForInput)
            sb.AppendLine(terminal.Interactive
                ? "Answer the prompt with WriteStdin, or send Ctrl+C to stop the command."
                : "Its stdin is closed. Send Ctrl+C with WriteStdin to stop it, then rerun it with piped input or a non-interactive option.");
        sb.Append("</terminal_notification>");
        return sb.ToString();
    }

    private static string FormatWallTime(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalMinutes >= 1
            ? $"{(int)time.TotalMinutes}m {time.Seconds}s"
            : $"{time.TotalSeconds:0.#}s";
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
