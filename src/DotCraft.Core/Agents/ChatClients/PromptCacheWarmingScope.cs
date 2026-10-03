namespace DotCraft.Agents;

internal sealed class PromptCacheWarmingScope : IDisposable
{
    private static readonly AsyncLocal<PromptCacheWarmingScope?> CurrentScope = new();

    private readonly PromptCacheWarmingScope? _previous;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _pending;
    private bool _disposed;

    private PromptCacheWarmingScope(PromptCacheWarmingScope? previous) => _previous = previous;

    public static PromptCacheWarmingScope? Current => CurrentScope.Value;

    public static PromptCacheWarmingScope Begin()
    {
        var scope = new PromptCacheWarmingScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public void Cancel()
    {
        lock (_gate)
            CancelPending();
    }

    public bool TryArm(out CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            CancelPending();
            if (_disposed)
            {
                cancellationToken = CancellationToken.None;
                return false;
            }

            _pending = new CancellationTokenSource();
            cancellationToken = _pending.Token;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            CancelPending();
        }

        if (ReferenceEquals(CurrentScope.Value, this))
            CurrentScope.Value = _previous;
    }

    private void CancelPending()
    {
        _pending?.Cancel();
        _pending = null;
    }
}
