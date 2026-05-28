using System.Collections.Concurrent;

namespace DotCraft.Protocol;

public sealed class ToolExecutionRuntimeContext
{
    public required string TurnId { get; init; }

    public required SessionTurn Turn { get; init; }

    public required Func<int> NextItemSequence { get; init; }

    public required Action<SessionItem> EmitItemStarted { get; init; }

    public required Action<SessionItem> EmitItemCompleted { get; init; }

    public required bool SupportsToolExecutionLifecycle { get; init; }

    private readonly ConcurrentDictionary<string, PendingToolExecutionRegistration> _pending =
        new(StringComparer.Ordinal);

    public void RegisterPending(PendingToolExecutionRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(registration.CallId))
            return;

        _pending[registration.CallId] = registration;
    }

    public PendingToolExecutionRegistration? TryClaimPending(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
            return null;

        return _pending.TryRemove(callId, out var registration)
            ? registration
            : null;
    }
}

public sealed class PendingToolExecutionRegistration
{
    public required string CallId { get; init; }

    public required string ToolName { get; init; }

    public required SessionItem Item { get; init; }
}

public static class ToolExecutionRuntimeScope
{
    private static readonly AsyncLocal<ToolExecutionRuntimeContext?> CurrentContext = new();

    public static ToolExecutionRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(ToolExecutionRuntimeContext context)
    {
        CurrentContext.Value = context;
        return new ScopeHandle();
    }

    private sealed class ScopeHandle : IDisposable
    {
        public void Dispose() => CurrentContext.Value = null;
    }
}
