namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal string? GetSubAgentParentThreadId(string threadId) => _metadataStore.GetSubAgentParentThreadId(threadId);

    internal async Task<SessionThread?> ReadThreadLifecycleMetadataAsync(
        string threadId, CancellationToken ct, bool forDeletion)
    {
        using var gate = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        var path = _rolloutStore.ResolveExistingPath(threadId);
        if (path is null && !forDeletion)
            return null;

        var thread = _metadataStore.ReadLifecycleMetadata(threadId, path, path is null ? null : new FileInfo(path).Length);
        if (path is null)
            return thread ?? (_metadataStore.GetRolloutPath(threadId) is not null ? NewLifecycleThread(threadId) : null);

        if (forDeletion || thread is null)
        {
            var header = ReadOwnedRolloutHeader(threadId, path, forDeletion);
            if (!forDeletion && header?.Source is null)
                throw new InvalidDataException("The canonical thread header has no source.");
            thread ??= NewLifecycleThread(threadId, header);
        }

        thread.Status = _rolloutStore.IsArchivedPath(path) ? ThreadStatus.Archived : ThreadStatus.Active;
        return thread;
    }

    private void VerifyDeletionIdentity(string threadId)
    {
        foreach (var archived in new[] { false, true })
        {
            var path = _rolloutStore.GetExpectedPath(threadId, archived);
            if (File.Exists(path))
                ReadOwnedRolloutHeader(threadId, path, forDeletion: true);
        }
    }

    private ThreadOpenedPayload? ReadOwnedRolloutHeader(string threadId, string path, bool forDeletion)
    {
        var header = ThreadSummaryScanner.ReadHeader(path);
        if (header is not null
            ? string.Equals(header.ThreadId, threadId, StringComparison.Ordinal)
            : forDeletion && string.Equals(_metadataStore.GetRolloutPath(threadId), path, PathComparison))
            return header;

        throw new InvalidDataException("The rollout does not establish ownership by the requested thread.");
    }

    private SessionThread NewLifecycleThread(string threadId, ThreadOpenedPayload? header = null)
    {
        var thread = new SessionThread { Id = threadId, WorkspacePath = StateDatabase.WorkspacePath };
        if (header is null)
            return thread;

        if (header.Source is not null)
            thread.Source = PersistedThreadSourceCodec.Decode(header.Source);
        thread.UserId = header.UserId;
        thread.OriginChannel = header.OriginChannel;
        thread.ChannelContext = header.ChannelContext;
        thread.ForkedFromId = header.ForkedFromId;
        thread.CreatedAt = header.CreatedAt;
        thread.LastActiveAt = header.LastActiveAt;
        thread.Metadata = header.Metadata;
        return thread;
    }
}
