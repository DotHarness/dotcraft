namespace DotCraft.AppServer;

/// <summary>Owns the AppServer Host's monotonic plugin-management snapshot revision.</summary>
internal sealed class PluginManagementSnapshotClock
{
    private long _revision;

    /// <summary>Raises the clock to a subsystem revision without minting a new one.</summary>
    public long Observe(long revision)
    {
        while (true)
        {
            var current = Volatile.Read(ref _revision);
            if (revision <= current)
                return current;
            if (Interlocked.CompareExchange(ref _revision, revision, current) == current)
                return revision;
        }
    }

    /// <summary>Mints the revision for a committed batch, always above anything observed.</summary>
    public long Advance(long observedRevision)
    {
        while (true)
        {
            var current = Volatile.Read(ref _revision);
            var next = Math.Max(current, observedRevision) + 1;
            if (Interlocked.CompareExchange(ref _revision, next, current) == current)
                return next;
        }
    }
}
