using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DotCraft.Tools.BackgroundTerminals;

public sealed partial class BackgroundTerminalService
{
    internal const int CompletionNoticeTailChars = 2000;

    private readonly ConcurrentDictionary<string, TerminalModelReadState> _modelReads = new(StringComparer.Ordinal);
    private volatile bool _disposing;

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
        var state = ModelReadStateFor(sessionId);

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

        state.EnterWait();
        OutputMatchWatch? watch = null;
        try
        {
            var outcome = ProcessWaitOutcome.Exited;
            if (active != null && !active.Process.HasExited && waitMs > 0)
            {
                watch = waitFor == null ? null : new OutputMatchWatch(active, waitFor, state.Cursor);
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
            // The exit may have landed after this call read a running status.
            if (state.LeaveWait(_disposing) && active != null)
                RaiseCompletionNotice(active, state);
        }
    }

    private TerminalModelReadState ModelReadStateFor(string sessionId) =>
        _modelReads.GetOrAdd(sessionId, static _ => new TerminalModelReadState());

    private async Task<BackgroundTerminalSnapshot> ReadForModelAsync(
        string sessionId,
        ActiveTerminal? active,
        TerminalModelReadState state,
        int limit,
        CancellationToken ct)
    {
        var from = state.Cursor;

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

        state.Read(slice.End, snapshot.Status != BackgroundTerminalStatus.Running);
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
        var running = snapshot.Status == BackgroundTerminalStatus.Running;
        var armed = running && terminal.Request.NotifyOnCompletion;
        var state = ModelReadStateFor(terminal.SessionId);
        if (state.HandOff(end, running, armed))
            RaiseCompletionNotice(terminal, state);
        return armed ? snapshot with { CompletionNoticeArmed = true } : snapshot;
    }

    private void OnTerminalCompletedForModel(ActiveTerminal terminal)
    {
        var state = ModelReadStateFor(terminal.SessionId);
        if (state.Complete(_disposing))
            RaiseCompletionNotice(terminal, state);
    }

    private void RaiseCompletionNotice(ActiveTerminal terminal, TerminalModelReadState state) =>
        RaiseModelNotice(terminal, state, BackgroundTerminalEventTypes.CompletionNotice);

    private void OnTerminalStalledForModel(ActiveTerminal terminal)
    {
        if (_modelReads.TryGetValue(terminal.SessionId, out var state) && state.OwesStallNotice(_disposing))
            RaiseModelNotice(terminal, state, BackgroundTerminalEventTypes.StallNotice);
    }

    // A notice can be dropped by Session Core, so it never moves the model read cursor.
    private void RaiseModelNotice(ActiveTerminal terminal, TerminalModelReadState state, string eventType)
    {
        var snapshot = terminal.CreateSnapshot(includeOutput: false);
        var slice = terminal.Slice(state.Cursor, CompletionNoticeTailChars, tailOnly: true);
        Raise(
            eventType,
            snapshot with
            {
                Output = slice.Text,
                OriginalOutputChars = slice.NewChars,
                Truncated = slice.Truncated,
                Lossy = slice.Lossy,
                WaitingForInput = eventType == BackgroundTerminalEventTypes.StallNotice
            },
            null);
    }

    private void SettleModelRead(string sessionId) => ModelReadStateFor(sessionId).Settle();
}
