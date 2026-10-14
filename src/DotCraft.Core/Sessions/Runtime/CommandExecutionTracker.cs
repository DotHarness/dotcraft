using DotCraft.Tools.BackgroundTerminals;

namespace DotCraft.Sessions;

internal sealed class CommandExecutionTracker
{
    private readonly SessionItem _item;
    private readonly DateTimeOffset _startedAt;
    private readonly TerminalOutputBuffer _aggregated = new();
    private readonly object _sync = new();
    private readonly Action<SessionItem, object> _emitItemDelta;
    private readonly Action<SessionItem> _emitItemCompleted;
    private readonly Func<SessionItem, Task>? _persistItemAfterTurnAsync;

    private CommandExecutionTracker(
        SessionItem item,
        DateTimeOffset startedAt,
        Action<SessionItem, object> emitItemDelta,
        Action<SessionItem> emitItemCompleted,
        Func<SessionItem, Task>? persistItemAfterTurnAsync)
    {
        _item = item;
        _startedAt = startedAt;
        _emitItemDelta = emitItemDelta;
        _emitItemCompleted = emitItemCompleted;
        _persistItemAfterTurnAsync = persistItemAfterTurnAsync;
    }

    public string? CallId => _item.AsCommandExecution?.CallId;

    public static bool CompletePendingFailureByCallId(string callId, string message)
    {
        var runtime = CommandExecutionRuntimeScope.Current;
        if (runtime is null || string.IsNullOrWhiteSpace(callId))
            return false;

        runtime.TryClaimPendingShellExecutionByCallId(callId);

        var registration = runtime.TryClaimPendingByCallId(callId);
        if (registration is null)
            return false;

        var item = registration.Item;
        var currentPayload = item.AsCommandExecution;
        var completedAt = DateTimeOffset.UtcNow;
        item.Status = ItemStatus.Completed;
        item.CompletedAt = completedAt;
        item.Payload = new CommandExecutionPayload
        {
            Command = currentPayload?.Command ?? registration.Command,
            WorkingDirectory = currentPayload?.WorkingDirectory ?? registration.WorkingDirectory,
            Source = currentPayload?.Source ?? registration.Source,
            Status = "failed",
            AggregatedOutput = message,
            SessionId = currentPayload?.SessionId,
            OutputPath = currentPayload?.OutputPath,
            OriginalOutputChars = currentPayload?.OriginalOutputChars,
            Truncated = currentPayload?.Truncated,
            BackgroundReason = currentPayload?.BackgroundReason,
            ExitCode = null,
            DurationMs = (long)Math.Max(0, (completedAt - item.CreatedAt).TotalMilliseconds),
            CallId = currentPayload?.CallId ?? registration.CallId
        };
        runtime.EmitItemCompleted(item);
        return true;
    }

    public static CommandExecutionTracker? Begin(string command, string workingDirectory, string source)
    {
        var runtime = CommandExecutionRuntimeScope.Current;
        if (runtime is null || !runtime.SupportsCommandExecutionStreaming)
            return null;

        var item = runtime.TryClaimPending(command, workingDirectory)?.Item;
        if (item == null)
        {
            item = new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(runtime.NextItemSequence()),
                TurnId = runtime.TurnId,
                Type = ItemType.CommandExecution,
                Status = ItemStatus.Started,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = new CommandExecutionPayload
                {
                    Command = command,
                    WorkingDirectory = workingDirectory,
                    Source = source,
                    Status = "inProgress",
                    AggregatedOutput = string.Empty
                }
            };
            runtime.Turn.Items.Add(item);
            runtime.EmitItemStarted(item);
        }
        else if (item.AsCommandExecution is { } existing && !string.Equals(existing.Source, source, StringComparison.Ordinal))
        {
            item.Payload = existing with { Source = source, WorkingDirectory = workingDirectory, Command = command };
        }

        return new CommandExecutionTracker(
            item,
            DateTimeOffset.UtcNow,
            runtime.EmitItemDelta,
            runtime.EmitItemCompleted,
            runtime.PersistItemAfterTurnAsync);
    }

    public void Append(string text, bool mirrorsTerminalOutput = false)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_sync)
        {
            _aggregated.Append(text);
        }

        _emitItemDelta(_item, new CommandExecutionOutputDelta
        {
            TextDelta = text,
            MirrorsTerminalOutput = mirrorsTerminalOutput
        });
    }

    public void Complete(
        string aggregatedOutput,
        string status,
        int? exitCode,
        string? sessionId = null,
        string? outputPath = null,
        int? originalOutputChars = null,
        bool? truncated = null,
        string? backgroundReason = null)
    {
        var currentPayload = _item.AsCommandExecution;
        var effectiveOutput = string.IsNullOrEmpty(aggregatedOutput)
            ? _aggregated.Snapshot(0).Output
            : aggregatedOutput;
        var completedAt = DateTimeOffset.UtcNow;
        _item.Status = ItemStatus.Completed;
        _item.CompletedAt = completedAt;
        _item.Payload = new CommandExecutionPayload
        {
            Command = currentPayload?.Command ?? string.Empty,
            WorkingDirectory = currentPayload?.WorkingDirectory ?? string.Empty,
            Source = currentPayload?.Source ?? "host",
            Status = status,
            AggregatedOutput = effectiveOutput,
            SessionId = sessionId ?? currentPayload?.SessionId,
            OutputPath = outputPath ?? currentPayload?.OutputPath,
            OriginalOutputChars = originalOutputChars ?? currentPayload?.OriginalOutputChars,
            Truncated = truncated ?? currentPayload?.Truncated,
            BackgroundReason = backgroundReason ?? currentPayload?.BackgroundReason,
            ExitCode = exitCode,
            DurationMs = (long)Math.Max(0, (completedAt - _startedAt).TotalMilliseconds),
            CallId = currentPayload?.CallId
        };
        _emitItemCompleted(_item);
    }

    public static string StatusOf(BackgroundTerminalSnapshot snapshot) => snapshot.Status switch
    {
        BackgroundTerminalStatus.Completed => "completed",
        BackgroundTerminalStatus.Killed or BackgroundTerminalStatus.TimedOut => "cancelled",
        BackgroundTerminalStatus.Lost => "lost",
        _ => "failed"
    };

    public void RecordTerminal(BackgroundTerminalSnapshot snapshot)
    {
        if (_item.AsCommandExecution is { } payload)
            _item.Payload = payload with { SessionId = snapshot.SessionId, OutputPath = snapshot.OutputPath };
    }

    public async Task CompleteFromTerminalAsync(BackgroundTerminalSnapshot snapshot, string aggregatedOutput, string status)
    {
        Complete(
            aggregatedOutput,
            status,
            snapshot.ExitCode,
            snapshot.SessionId,
            snapshot.OutputPath,
            snapshot.OriginalOutputChars,
            snapshot.Truncated,
            snapshot.BackgroundReason);
        if (_persistItemAfterTurnAsync is not null)
            await _persistItemAfterTurnAsync(_item);
    }
}
