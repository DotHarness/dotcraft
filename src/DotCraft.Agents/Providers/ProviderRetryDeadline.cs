namespace DotCraft.Agents;

/// <summary>A process-local retry deadline measured using elapsed, rather than wall-clock, time.</summary>
public sealed class ProviderRetryDeadline
{
    private readonly TimeProvider _clock;
    private readonly long _started;

    private ProviderRetryDeadline(TimeSpan delay, TimeProvider clock)
    {
        Delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        _clock = clock;
        _started = clock.GetTimestamp();
    }

    public TimeSpan Delay { get; }

    public TimeSpan Remaining
    {
        get
        {
            var elapsed = _clock.GetElapsedTime(_started);
            return elapsed >= Delay ? TimeSpan.Zero : Delay - (elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed);
        }
    }

    public static ProviderRetryDeadline FromDelay(TimeSpan delay, TimeProvider? clock = null) =>
        new(delay, clock ?? TimeProvider.System);
}
