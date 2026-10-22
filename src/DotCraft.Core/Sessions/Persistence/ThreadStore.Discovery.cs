using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal long ProjectedRolloutBytes => _historyProjectionStore.ProjectedBytes;

    /// <summary>
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// Rows whose canonical rollout file is missing are omitted because callers cannot read them.
    /// </summary>
    public async Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        await _reconcileGate.WaitAsync(ct);
        try
        {
            var paths = _rolloutStore.EnumerateRolloutPaths().ToList();
            try
            {
                var projectionStates = _metadataStore.LoadProjectionStates();
                var threadIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, state) in projectionStates)
                    threadIdsByPath.TryAdd(Path.GetFullPath(state.RolloutPath), id);
                var unreadableThreadIds = new HashSet<string>(StringComparer.Ordinal);
                var unreadablePaths = 0;
                var cleanupCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in paths)
                {
                    ct.ThrowIfCancellationRequested();
                    var threadId = ResolveDiscoveredThreadId(path, threadIdsByPath);
                    if (threadId == null)
                    {
                        unreadablePaths++;
                        continue;
                    }

                    try
                    {
                        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
                        var file = new FileInfo(path);
                        if (!file.Exists)
                            continue;
                        if (projectionStates.TryGetValue(threadId, out var state)
                            && string.Equals(Path.GetFullPath(state.RolloutPath), path, StringComparison.OrdinalIgnoreCase)
                            && state.ProjectedRolloutOffset == file.Length
                            && state.AttachmentIndexVersion == ThreadAttachmentStore.IndexVersion)
                        {
                            continue;
                        }

                        cleanupCandidates.UnionWith(
                            await _historyProjectionStore.ProjectCommittedAsync(threadId, path, file.Length, ct));
                    }
                    catch (ThreadHistoryUnavailableException ex)
                    {
                        unreadableThreadIds.Add(threadId);
                        System.Diagnostics.Trace.TraceWarning(
                            $"Unable to repair rollout '{Path.GetFileName(path)}': {ex.InnerException?.Message ?? ex.Message}");
                    }
                }

                _attachmentStore.CleanupCandidates(cleanupCandidates);
                if (unreadableThreadIds.Count == 0
                    && unreadablePaths == 0
                    && DateTimeOffset.UtcNow - _lastOrphanSweep >= OrphanSweepInterval)
                {
                    _lastOrphanSweep = DateTimeOffset.UtcNow;
                    _attachmentStore.CleanupUnreferencedAttachments(OrphanAttachmentAge);
                }

                return _metadataStore.LoadIndex()
                    .Where(summary => !unreadableThreadIds.Contains(summary.Id)
                                      && _rolloutStore.ResolveExistingPath(summary.Id) != null)
                    .ToList();
            }
            catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException
                                               or InvalidOperationException
                                               or JsonException
                                               or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning($"Thread metadata read-repair failed; using rollout files: {ex.Message}");
                return await LoadFilesystemIndexAsync(paths, ct);
            }
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    internal async Task<ThreadHistoryAggregates> ReadHistoryAggregatesAsync(
        string threadId,
        CancellationToken ct = default)
    {
        using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        return await _historyProjectionStore.ReadAggregatesAsync(threadId, ResolvePersistedHistoryPath(threadId), ct);
    }

    private string? ResolveDiscoveredThreadId(string path, IReadOnlyDictionary<string, string> threadIdsByPath)
    {
        if (threadIdsByPath.TryGetValue(path, out var known))
            return known;
        try
        {
            return ThreadSummaryScanner.ReadThreadId(path) is { } threadId
                   && string.Equals(ThreadRolloutStore.BuildFileName(threadId), Path.GetFileName(path), StringComparison.Ordinal)
                ? threadId
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<List<ThreadSummary>> LoadFilesystemIndexAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct)
    {
        var summaries = new List<ThreadSummary>();
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await ThreadSummaryScanner.ScanAsync(path, ct) is { } summary)
                    summaries.Add(summary);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or NotSupportedException)
            {
                System.Diagnostics.Trace.TraceWarning($"Unable to read rollout '{Path.GetFileName(path)}': {ex.Message}");
            }
        }

        return summaries
            .OrderByDescending(static summary => summary.LastActiveAt)
            .ThenByDescending(static summary => summary.Id, StringComparer.Ordinal)
            .ToList();
    }
}
