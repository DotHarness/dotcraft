namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal async Task<bool> ArchiveStoredThreadAsync(
        SessionThread thread, bool requireRollout, CancellationToken ct = default)
    {
        var threadId = thread.Id;
        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var path = requireRollout
            ? ResolvePersistedHistoryPath(threadId)
            : _rolloutStore.ResolveExistingPath(threadId);
        if (path is null || _rolloutStore.IsArchivedPath(path))
        {
            thread.Status = ThreadStatus.Archived;
            return path is not null;
        }

        await _rolloutStore.CloseThreadAsync(threadId, CancellationToken.None);
        var previousOffset = new FileInfo(path).Length;
        var result = await TrackPersistedAsync(
            threadId,
            () => _rolloutStore.ArchiveStoredThreadAsync(threadId, path, previousOffset, ct));
        thread.Status = ThreadStatus.Archived;
        _historyProjectionStore.ProjectArchive(threadId, path, previousOffset, result);
        return true;
    }
}
