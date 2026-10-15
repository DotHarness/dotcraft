using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DotCraft.Tools.BackgroundTerminals;

public sealed partial class BackgroundTerminalService
{
    internal const int CompletionNoticeTailChars = 2000;

    private readonly ConcurrentDictionary<string, ModelReadState> _modelReads = new(StringComparer.Ordinal);
    private volatile bool _disposing;

    private sealed class ModelReadState
    {
        public readonly object Sync = new();
        public long Cursor;
        public int Waiters;
        public bool HandedOff;
        public bool Completed;
        public bool Settled;
    }

    public async Task<BackgroundTerminalSnapshot> InteractAsync(
        string sessionId,
        string input,
        int waitMs,
        int? maxOutputChars = null,
        Regex? waitFor = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var limit = maxOutputChars ?? _config.DefaultReadMaxOutputChars;
        _active.TryGetValue(sessionId, out var active);
        if (active == null)
            _ = GetMetadata(sessionId);
        var state = _modelReads.GetOrAdd(sessionId, static _ => new ModelReadState());

        if (IsStopRequest(input))
        {
            SettleModelRead(sessionId);
            if (active != null)
                await KillAsync(active, BackgroundTerminalStatus.Killed, ct).ConfigureAwait(false);
            return await ReadForModelAsync(sessionId, active, state, limit, ct).ConfigureAwait(false);
        }

        if (input.Length > 0)
        {
            if (active == null)
                throw new KeyNotFoundException($"Background terminal '{sessionId}' is not running.");
            if (active.Process.HasExited)
                throw new InvalidOperationException($"Background terminal '{sessionId}' has exited and cannot accept input.");
            await active.Process.StandardInput.WriteAsync(input).ConfigureAwait(false);
            await active.Process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            active.ClearStall();
        }

        lock (state.Sync)
            state.Waiters++;
        OutputMatchWatch? watch = null;
        try
        {
            var outcome = ProcessWaitOutcome.Exited;
            if (active != null && !active.Process.HasExited && waitMs > 0)
            {
                long from;
                lock (state.Sync)
                    from = state.Cursor;
                watch = waitFor == null ? null : new OutputMatchWatch(active, waitFor, from);
                var wait = TimeSpan.FromMilliseconds(Math.Min(waitMs, Math.Max(1, _config.MaxYieldTimeMs)));
                outcome = await WaitForExitOrDelayAsync(
                    active.Process,
                    wait,
                    ct,
                    matched: watch?.Token ?? default,
                    stalled: active.StallToken).ConfigureAwait(false);
                if (outcome == ProcessWaitOutcome.CallerCancelled)
                    ct.ThrowIfCancellationRequested();
            }
            if (active?.Process.HasExited == true)
                await active.WaitForCompletionMetadataAsync(ct).ConfigureAwait(false);

            var snapshot = await ReadForModelAsync(sessionId, active, state, limit, ct).ConfigureAwait(false);
            return snapshot with
            {
                MatchedLine = watch?.MatchedLine,
                WaitingForInput = outcome == ProcessWaitOutcome.Stalled
            };
        }
        finally
        {
            watch?.Dispose();
            bool notify;
            lock (state.Sync)
            {
                state.Waiters--;
                // The exit may have landed after this call read a running status.
                notify = state.Waiters == 0 && state.Completed && state.HandedOff && !state.Settled && !_disposing;
                if (notify)
                    state.Settled = true;
            }
            if (notify && active != null)
                RaiseCompletionNotice(active, state);
        }
    }

    private static bool IsStopRequest(string input)
    {
        var trimmed = input.Trim('\r', '\n');
        return trimmed.Length > 0 && trimmed.All(static c => c == '\u0003');
    }

    private async Task<BackgroundTerminalSnapshot> ReadForModelAsync(
        string sessionId,
        ActiveTerminal? active,
        ModelReadState state,
        int limit,
        CancellationToken ct)
    {
        long from;
        lock (state.Sync)
            from = state.Cursor;

        // Status is read before output: a final status means output was already drained.
        BackgroundTerminalSnapshot snapshot;
        TerminalOutputSlice slice;
        if (active != null)
        {
            snapshot = active.CreateSnapshot(maxOutputChars: limit, includeOutput: false);
            slice = active.Slice(from, limit);
        }
        else
        {
            var metadata = GetMetadata(sessionId);
            snapshot = metadata.ToSnapshot(string.Empty, 0, false);
            var buffer = await TerminalOutputBuffer.ReadAsync(metadata.OutputPath, ct).ConfigureAwait(false);
            slice = buffer.Slice(from, limit);
        }

        lock (state.Sync)
        {
            state.Cursor = Math.Max(state.Cursor, slice.End);
            if (snapshot.Status != BackgroundTerminalStatus.Running)
                state.Settled = true;
        }

        return snapshot with
        {
            Output = slice.Text,
            OriginalOutputChars = slice.NewChars,
            Truncated = slice.Truncated,
            Lossy = slice.Lossy
        };
    }

    private BackgroundTerminalSnapshot HandToModel(ActiveTerminal terminal, BackgroundTerminalSnapshot snapshot, long end)
    {
        var state = _modelReads.GetOrAdd(terminal.SessionId, static _ => new ModelReadState());
        var running = snapshot.Status == BackgroundTerminalStatus.Running;
        var armed = running && terminal.Request.NotifyOnCompletion;
        var notify = false;
        lock (state.Sync)
        {
            state.Cursor = Math.Max(state.Cursor, end);
            if (!running)
                state.Settled = true;
            else if (armed)
            {
                state.HandedOff = true;
                notify = state.Completed && !state.Settled;
                if (notify)
                    state.Settled = true;
            }
        }

        if (notify)
            RaiseCompletionNotice(terminal, state);
        return armed ? snapshot with { CompletionNoticeArmed = true } : snapshot;
    }

    private void OnTerminalCompletedForModel(ActiveTerminal terminal)
    {
        if (!_modelReads.TryGetValue(terminal.SessionId, out var state))
            return;
        bool notify;
        lock (state.Sync)
        {
            state.Completed = true;
            notify = state.HandedOff && !state.Settled && state.Waiters == 0 && !_disposing;
            if (notify)
                state.Settled = true;
        }

        if (notify)
            RaiseCompletionNotice(terminal, state);
    }

    private void RaiseCompletionNotice(ActiveTerminal terminal, ModelReadState state) =>
        RaiseModelNotice(terminal, state, BackgroundTerminalEventTypes.CompletionNotice);

    private void OnTerminalStalledForModel(ActiveTerminal terminal)
    {
        if (!_modelReads.TryGetValue(terminal.SessionId, out var state))
            return;
        bool notify;
        lock (state.Sync)
            notify = state.HandedOff && !state.Settled && state.Waiters == 0 && !_disposing;
        if (notify)
            RaiseModelNotice(terminal, state, BackgroundTerminalEventTypes.StallNotice);
    }

    private void RaiseModelNotice(ActiveTerminal terminal, ModelReadState state, string eventType)
    {
        var completion = eventType == BackgroundTerminalEventTypes.CompletionNotice;
        long from;
        lock (state.Sync)
            from = state.Cursor;
        var snapshot = terminal.CreateSnapshot(includeOutput: false);
        var slice = terminal.Slice(from, CompletionNoticeTailChars, tailOnly: true);
        if (completion && !slice.Truncated && !slice.Lossy)
        {
            lock (state.Sync)
                state.Cursor = Math.Max(state.Cursor, slice.End);
        }

        Raise(
            eventType,
            snapshot with
            {
                Output = slice.Text,
                OriginalOutputChars = slice.NewChars,
                Truncated = slice.Truncated,
                Lossy = slice.Lossy,
                WaitingForInput = !completion
            },
            null);
    }

    private void SettleModelRead(string sessionId)
    {
        var state = _modelReads.GetOrAdd(sessionId, static _ => new ModelReadState());
        lock (state.Sync)
            state.Settled = true;
    }
}
