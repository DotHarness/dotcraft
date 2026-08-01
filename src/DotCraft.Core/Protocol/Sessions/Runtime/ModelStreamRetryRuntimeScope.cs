namespace DotCraft.Protocol;

/// <summary>
/// Runtime callbacks used by model-stream retry wrappers to report transient
/// provider stream reconnection attempts to the active turn.
/// </summary>
public sealed class ModelStreamRetryRuntimeContext
{
    /// <summary>
    /// Reports that the current sampling request will be retried.
    /// </summary>
    public required Action<int, int, Exception> NotifyRetry { get; init; }

    /// <summary>
    /// Reports that stream retry handling is giving up on the current failure.
    /// </summary>
    public Action<Exception>? NotifyFinalFailure { get; init; }

    /// <summary>
    /// Reports that a retryable stream failure was not retried because replaying
    /// the sampling request could duplicate already-emitted visible output or
    /// tool effects.
    /// </summary>
    public Action<Exception, string>? NotifyRetrySuppressed { get; init; }

    /// <summary>
    /// Reports the sanitized outcome of one completed provider stream attempt.
    /// </summary>
    public Action<ModelStreamAttemptDiagnostic>? NotifyAttemptCompleted { get; init; }
}

/// <summary>
/// Sanitized provider stream attempt data exposed to the active Session Core turn.
/// </summary>
public sealed record ModelStreamAttemptDiagnostic(
    int? RequestIndex,
    int AttemptNumber,
    int RetryLimit,
    string Outcome,
    string RetryDecision,
    string? FailureKind,
    double DurationMs,
    bool VisibleOutputEmitted,
    int? StatusCode,
    string? RequestId,
    string? SessionIdHash,
    string? ThreadIdHash,
    string? PromptCacheKeyHash);

/// <summary>
/// Async-local bridge between provider-neutral chat clients and Session Core
/// turn events.
/// </summary>
public static class ModelStreamRetryRuntimeScope
{
    private static readonly AsyncLocal<ModelStreamRetryRuntimeContext?> CurrentContext = new();

    public static ModelStreamRetryRuntimeContext? Current => CurrentContext.Value;

    public static IDisposable Set(ModelStreamRetryRuntimeContext context)
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = context;
        return new Scope(previous);
    }

    private sealed class Scope(ModelStreamRetryRuntimeContext? previous) : IDisposable
    {
        public void Dispose() => CurrentContext.Value = previous;
    }
}
