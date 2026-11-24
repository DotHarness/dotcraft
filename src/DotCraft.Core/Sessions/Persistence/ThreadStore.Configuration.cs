using DotCraft.Tools;

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

    internal async Task CommitDynamicToolDeclarationsAsync(
        SessionThread thread, IReadOnlyList<ThreadDynamicToolDeclaration> declarations, CancellationToken ct)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, thread.Id, ct);
        var updated = thread.WithDynamicToolDeclarations(declarations);
        var result = await TrackPersistedAsync(
            thread.Id, () => _rolloutStore.AppendThreadBaselineAsync(updated, ct), thread.Turns);
        thread.DynamicToolDeclarations = declarations;
        await TryProjectHistoryAsync(thread.Id, result, CancellationToken.None);
    }
}
