namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal async Task CommitConfigurationAsync(
        SessionThread thread, ThreadConfiguration configuration, CancellationToken ct)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var result = await SaveThreadUnderGateAsync(thread.WithConfiguration(configuration), ct);
        // Background Turn saves must observe the new configuration before this write gate is released.
        thread.Configuration = configuration;
        await TryProjectHistoryAsync(thread.Id, result, CancellationToken.None);
    }
}
