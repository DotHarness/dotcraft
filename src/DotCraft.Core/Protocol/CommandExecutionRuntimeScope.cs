using System.Collections.Concurrent;

namespace DotCraft.Protocol;

public sealed class CommandExecutionRuntimeContext
{
    public required string ThreadId { get; init; }

    public required string TurnId { get; init; }

    public required SessionTurn Turn { get; init; }

    public required Func<int> NextItemSequence { get; init; }

    public required Action<SessionItem> EmitItemStarted { get; init; }

    public required Action<SessionItem, object> EmitItemDelta { get; init; }

    public required Action<SessionItem> EmitItemCompleted { get; init; }

    public required bool SupportsCommandExecutionStreaming { get; init; }

    private readonly ConcurrentQueue<PendingCommandExecutionRegistration> _pending = new();

    private readonly ConcurrentQueue<PendingShellExecutionRegistration> _pendingShellExecutions = new();

    public void RegisterPending(PendingCommandExecutionRegistration registration) => _pending.Enqueue(registration);

    public void RegisterPendingShellExecution(PendingShellExecutionRegistration registration) =>
        _pendingShellExecutions.Enqueue(registration);

    public PendingCommandExecutionRegistration? TryClaimPending(
        string command,
        string workingDirectory)
    {
        if (_pending.IsEmpty)
            return null;

        var remaining = new Queue<PendingCommandExecutionRegistration>();
        PendingCommandExecutionRegistration? match = null;

        while (_pending.TryDequeue(out var entry))
        {
            if (match == null &&
                string.Equals(entry.Command, command, StringComparison.Ordinal) &&
                string.Equals(entry.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                match = entry;
                continue;
            }

            remaining.Enqueue(entry);
        }

        while (remaining.Count > 0)
            _pending.Enqueue(remaining.Dequeue());

        return match;
    }

    public PendingCommandExecutionRegistration? TryClaimPendingByCallId(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId) || _pending.IsEmpty)
            return null;

        var remaining = new Queue<PendingCommandExecutionRegistration>();
        PendingCommandExecutionRegistration? match = null;

        while (_pending.TryDequeue(out var entry))
        {
            if (match == null && string.Equals(entry.CallId, callId, StringComparison.Ordinal))
            {
                match = entry;
                continue;
            }

            remaining.Enqueue(entry);
        }

        while (remaining.Count > 0)
            _pending.Enqueue(remaining.Dequeue());

        return match;
    }

    public PendingShellExecutionRegistration? TryClaimPendingShellExecution(
        string command,
        string workingDirectory)
    {
        if (_pendingShellExecutions.IsEmpty)
            return null;

        var remaining = new Queue<PendingShellExecutionRegistration>();
        PendingShellExecutionRegistration? match = null;

        while (_pendingShellExecutions.TryDequeue(out var entry))
        {
            if (match == null &&
                string.Equals(entry.Command, command, StringComparison.Ordinal) &&
                string.Equals(entry.WorkingDirectory, workingDirectory, StringComparison.OrdinalIgnoreCase))
            {
                match = entry;
                continue;
            }

            remaining.Enqueue(entry);
        }

        while (remaining.Count > 0)
            _pendingShellExecutions.Enqueue(remaining.Dequeue());

        return match;
    }

    public PendingShellExecutionRegistration? TryClaimPendingShellExecutionByCallId(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId) || _pendingShellExecutions.IsEmpty)
            return null;

        var remaining = new Queue<PendingShellExecutionRegistration>();
        PendingShellExecutionRegistration? match = null;

        while (_pendingShellExecutions.TryDequeue(out var entry))
        {
            if (match == null && string.Equals(entry.CallId, callId, StringComparison.Ordinal))
            {
                match = entry;
                continue;
            }

            remaining.Enqueue(entry);
        }

        while (remaining.Count > 0)
            _pendingShellExecutions.Enqueue(remaining.Dequeue());

        return match;
    }
}

public sealed class PendingCommandExecutionRegistration
{
    public required string CallId { get; init; }

    public required string Command { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string Source { get; init; }

    public required SessionItem Item { get; init; }
}

public sealed class PendingShellExecutionRegistration
{
    public required string CallId { get; init; }

    public required string Command { get; init; }

    public required string WorkingDirectory { get; init; }

    public required string Source { get; init; }
}

public static class CommandExecutionRuntimeScope
{
    private static readonly AsyncLocal<CommandExecutionRuntimeContext?> CurrentContext = new();

    public static CommandExecutionRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(CommandExecutionRuntimeContext context)
    {
        CurrentContext.Value = context;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentContext.Value = null;
    }
}
