using System.Text;

namespace DotCraft.Protocol;

internal sealed class CommandExecutionTracker
{
    private readonly SessionItem _item;
    private readonly DateTimeOffset _startedAt;
    private readonly StringBuilder _aggregated = new();
    private readonly object _sync = new();
    private readonly Action<SessionItem, object> _emitItemDelta;
    private readonly Action<SessionItem> _emitItemCompleted;

    private CommandExecutionTracker(
        SessionItem item,
        DateTimeOffset startedAt,
        Action<SessionItem, object> emitItemDelta,
        Action<SessionItem> emitItemCompleted)
    {
        _item = item;
        _startedAt = startedAt;
        _emitItemDelta = emitItemDelta;
        _emitItemCompleted = emitItemCompleted;
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

        return new CommandExecutionTracker(item, DateTimeOffset.UtcNow, runtime.EmitItemDelta, runtime.EmitItemCompleted);
    }

    public void Append(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        lock (_sync)
        {
            _aggregated.Append(text);
        }

        _emitItemDelta(_item, new CommandExecutionOutputDelta { TextDelta = text });
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
            ? _aggregated.ToString().TrimEnd('\r', '\n')
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
}
