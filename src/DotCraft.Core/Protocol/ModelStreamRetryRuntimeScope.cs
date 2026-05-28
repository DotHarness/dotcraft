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
}

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
