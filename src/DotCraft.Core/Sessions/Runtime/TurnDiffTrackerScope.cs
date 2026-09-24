namespace DotCraft.Sessions;

internal static class TurnDiffTrackerScope
{
    private static readonly AsyncLocal<TurnDiffTracker?> CurrentTracker = new();

    public static TurnDiffTracker? Current => CurrentTracker.Value;

    public static IDisposable Set(TurnDiffTracker tracker)
    {
        var previous = CurrentTracker.Value;
        CurrentTracker.Value = tracker;
        return new Scope(previous);
    }

    private sealed class Scope(TurnDiffTracker? previous) : IDisposable
    {
        public void Dispose() => CurrentTracker.Value = previous;
    }
}
