using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal long ProjectedRolloutBytes => _historyProjectionStore.ProjectedBytes;

    /// <summary>Returns persisted thread summaries without loading their history projections.</summary>
    public async Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        try
        {
            var excludedThreadIds = await ReconcileIndexAsync(ct);
            return _metadataStore.LoadIndex()
                .Where(summary => !excludedThreadIds.Contains(summary.Id))
                .ToList();
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException
                                           or InvalidOperationException
                                           or InvalidDataException
                                           or JsonException
                                           or NotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning($"Thread metadata read-repair failed; using rollout files: {ex.Message}");
            return await LoadFilesystemIndexAsync(_rolloutStore.EnumerateRolloutPaths().ToList(), ct);
        }
    }

    internal async Task<HashSet<string>> ReconcileIndexAsync(CancellationToken ct)
    {
        await _reconcileGate.WaitAsync(ct);
        try
        {
            var paths = _rolloutStore.EnumerateRolloutPaths().ToList();
            var projectionStates = _metadataStore.LoadProjectionStates();
            var threadIdsByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, state) in projectionStates)
                threadIdsByPath.TryAdd(Path.GetFullPath(state.RolloutPath), id);
            var excludedThreadIds = projectionStates.Keys.ToHashSet(StringComparer.Ordinal);
            var attachmentProjectionsCurrent = true;

            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                var threadId = ResolveDiscoveredThreadId(path, threadIdsByPath);
                if (threadId == null)
                {
                    attachmentProjectionsCurrent = false;
                    continue;
                }

                try
                {
                    var file = new FileInfo(path);
                    if (!file.Exists)
                    {
                        attachmentProjectionsCurrent = false;
                        continue;
                    }
                    if (projectionStates.TryGetValue(threadId, out var state)
                        && string.Equals(Path.GetFullPath(state.RolloutPath), path, StringComparison.OrdinalIgnoreCase)
                        && state.ProjectedRolloutOffset == file.Length)
                    {
                        excludedThreadIds.Remove(threadId);
                        attachmentProjectionsCurrent &= state.AttachmentIndexVersion == ThreadAttachmentStore.IndexVersion;
                        continue;
                    }

                    attachmentProjectionsCurrent = false;
                    using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
                    file.Refresh();
                    if (!file.Exists)
                        continue;
                    var scanned = await ThreadSummaryScanner.ScanMetadataAsync(path, ct);
                    if (scanned is null || !string.Equals(scanned.Summary.Id, threadId, StringComparison.Ordinal))
                        throw new InvalidDataException("The canonical thread header does not match its rollout identity.");
                    _metadataStore.UpsertDiscoveredSummary(scanned, path, file.Length);
                    excludedThreadIds.Remove(threadId);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
                {
                    attachmentProjectionsCurrent = false;
                    excludedThreadIds.Add(threadId);
                    System.Diagnostics.Trace.TraceWarning(
                        $"Unable to repair rollout summary '{Path.GetFileName(path)}': {ex.Message}");
                }
            }

            if (attachmentProjectionsCurrent
                && excludedThreadIds.Count == 0
                && DateTimeOffset.UtcNow - _lastOrphanSweep >= OrphanSweepInterval
                && _attachmentStore.CleanupUnreferencedAttachments(OrphanAttachmentAge))
            {
                _lastOrphanSweep = DateTimeOffset.UtcNow;
            }
            return excludedThreadIds;
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
        return await _historyProjectionStore.ReadAggregatesAsync(threadId, ResolvePersistedHistoryPath(threadId), [], ct);
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
                if ((await ThreadSummaryScanner.ScanMetadataAsync(path, ct).ConfigureAwait(false))?.Summary is { } summary)
                    summaries.Add(summary);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
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
