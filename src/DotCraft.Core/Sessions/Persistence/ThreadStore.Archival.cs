namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal async Task ArchiveStoredThreadAsync(string threadId, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var path = ResolvePersistedHistoryPath(threadId);
        var snapshot = await _historyProjectionStore.ReadSnapshotAsync(threadId, path, repair: true, ct);
        var persisted = snapshot.Thread;
        if (persisted.Status == ThreadStatus.Archived && _rolloutStore.IsArchivedPath(path))
            return;

        var result = await TrackPersistedAsync(
            threadId,
            () => _rolloutStore.ArchiveStoredThreadAsync(persisted, path, ct));
        await TryProjectHistoryAsync(threadId, result, ct);
    }
}
