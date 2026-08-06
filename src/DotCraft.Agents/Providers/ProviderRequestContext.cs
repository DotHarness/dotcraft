namespace DotCraft.Agents;

/// <summary>Provider-neutral kinds of model requests issued by Session Core.</summary>
public enum ProviderRequestKind
{
    Turn,
    Compaction,
    Memory
}

/// <summary>Provider-neutral identity for one model request.</summary>
public sealed record ProviderConversationIdentity(
    string CurrentThreadId,
    string RootThreadId,
    string? ParentThreadId,
    string? ForkedFromThreadId,
    string? TurnId,
    string ContextWindowId,
    ProviderRequestKind RequestKind,
    long TurnStartedAtUnixMs,
    string ThreadSource,
    string? SubagentKind);

/// <summary>Mutable request state shared between Session Core and a provider adapter.</summary>
public sealed class ProviderConversationState(ProviderConversationIdentity identity)
{
    private readonly object _gate = new();
    private ProviderConversationIdentity _identity = identity
        ?? throw new ArgumentNullException(nameof(identity));
    private string? _continuationState;

    public ProviderConversationIdentity Identity
    {
        get { lock (_gate) return _identity; }
    }

    public string? ContinuationState
    {
        get { lock (_gate) return _continuationState; }
    }

    public bool TryCaptureContinuationState(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized is null)
            return false;
        lock (_gate)
        {
            if (_continuationState is not null)
                return false;
            _continuationState = normalized;
            return true;
        }
    }

    public void AdvanceContextWindow(string contextWindowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextWindowId);
        lock (_gate)
            _identity = _identity with { ContextWindowId = contextWindowId.Trim() };
    }

    public IDisposable OverrideRequestKind(ProviderRequestKind requestKind)
    {
        ProviderRequestKind previous;
        lock (_gate)
        {
            previous = _identity.RequestKind;
            _identity = _identity with { RequestKind = requestKind };
        }
        return new RestoreScope(this, previous);
    }

    private void Restore(ProviderRequestKind requestKind)
    {
        lock (_gate)
            _identity = _identity with { RequestKind = requestKind };
    }

    private sealed class RestoreScope(ProviderConversationState owner, ProviderRequestKind previous) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Restore(previous);
        }
    }
}

/// <summary>Services and identity scoped to the active provider request.</summary>
public sealed record ProviderRequestContext(
    ProviderConversationIdentity ConversationIdentity,
    IProviderConversationHistory? History = null,
    IProviderCompactionBridge? Compaction = null,
    IModelRuntimeDiagnostics? Diagnostics = null,
    ProviderConversationState? ConversationState = null)
{
    public ProviderConversationIdentity CurrentIdentity =>
        ConversationState?.Identity ?? ConversationIdentity;
}

/// <summary>Flows provider-neutral request state across chat-client middleware.</summary>
public static class ProviderRequestContextScope
{
    private static readonly AsyncLocal<ProviderRequestContext?> CurrentContext = new();

    /// <summary>Gets the active request context.</summary>
    public static ProviderRequestContext? Current => CurrentContext.Value;

    /// <summary>Pushes a context and restores the preceding context when disposed.</summary>
    public static IDisposable Push(ProviderRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous, context);
    }

    private sealed class Scope(ProviderRequestContext? previous, ProviderRequestContext current) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (ReferenceEquals(CurrentContext.Value, current))
                CurrentContext.Value = previous;
        }
    }
}
