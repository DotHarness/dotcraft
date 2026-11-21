namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal async Task ArchiveStoredThreadAsync(string threadId, CancellationToken ct = default)
    {
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var path = ResolvePersistedHistoryPath(threadId);
        if (_rolloutStore.IsArchivedPath(path))
            return;

        await _rolloutStore.CloseThreadAsync(threadId, ct);
        var previousOffset = new FileInfo(path).Length;
        var result = await TrackPersistedAsync(
            threadId,
            () => _rolloutStore.ArchiveStoredThreadAsync(threadId, path, ct));
        _historyProjectionStore.ProjectArchive(threadId, path, previousOffset, result);
    }
}
