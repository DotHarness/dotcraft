namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    internal async Task<SessionThread?> LoadThreadWorkingSetAsync(string threadId, CancellationToken ct = default)
    {
        using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        if (_rolloutStore.ResolveExistingPath(threadId) is not { } path)
            return null;

        SessionThread? thread;
        try
        {
            thread = await _historyProjectionStore.ReadWorkingSetAsync(threadId, path, ct);
        }
        catch (ThreadHistoryUnavailableException ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Thread projection unavailable for '{threadId}'; replaying its rollout to load it: {ex.Message}");
            CountForwardReplay();
            thread = await ThreadRolloutStore.ReadThreadFileAsync(path, ct);
            if (thread != null)
                ThreadWorkingSet.Trim(thread);
        }

        return thread != null && string.Equals(thread.Id, threadId, StringComparison.Ordinal) ? thread : null;
    }

    internal async Task<IReadOnlyList<string>> ReadVisibleTurnIdsAsync(SessionThread thread, CancellationToken ct = default)
    {
        if (thread.OmittedTurnCount == 0)
            return thread.Turns.Select(static turn => turn.Id).ToList();

        var projected = await ReadProjectionAsync(
            thread.Id,
            path => _historyProjectionStore.ReadTurnIdsAsync(thread.Id, path, ct),
            ct);
        var known = projected.ToHashSet(StringComparer.Ordinal);
        return [.. projected, .. thread.Turns.Select(static turn => turn.Id).Where(known.Add)];
    }

    internal async Task<IReadOnlySet<string>> ReadVisibleTurnIdsAsync(
        SessionThread thread,
        IReadOnlyCollection<string> candidates,
        CancellationToken ct = default)
    {
        var resident = thread.Turns.Select(static turn => turn.Id).ToHashSet(StringComparer.Ordinal);
        var unresolved = candidates.Where(id => !resident.Contains(id)).Distinct(StringComparer.Ordinal).ToList();
        if (thread.OmittedTurnCount == 0 || unresolved.Count == 0)
            return resident;

        var projected = await ReadProjectionAsync(
            thread.Id,
            path => _historyProjectionStore.ReadExistingTurnIdsAsync(thread.Id, path, unresolved, ct),
            ct);
        resident.UnionWith(projected);
        return resident;
    }

    internal async Task<IReadOnlyList<SessionTurn>> ReadVisibleTurnsAsync(
        SessionThread thread,
        IReadOnlyCollection<string> turnIds,
        CancellationToken ct = default)
    {
        var resident = thread.Turns
            .Where(turn => turnIds.Contains(turn.Id, StringComparer.Ordinal))
            .ToList();
        var missing = turnIds
            .Where(id => resident.All(turn => !string.Equals(turn.Id, id, StringComparison.Ordinal)))
            .ToList();
        if (missing.Count == 0 || thread.OmittedTurnCount == 0)
            return resident;

        var projected = await ReadProjectionAsync(
            thread.Id,
            path => _historyProjectionStore.ReadTurnsAsync(thread.Id, path, missing, ct),
            ct);
        return [.. projected, .. resident];
    }

    internal Task<IReadOnlyList<SessionTurn>> ReadProjectedTurnsAsync(
        string threadId,
        IReadOnlyCollection<string> turnIds,
        CancellationToken ct = default) =>
        ReadProjectionAsync(threadId, path => _historyProjectionStore.ReadTurnsAsync(threadId, path, turnIds, ct), ct);

    internal Task<SessionItem?> ReadProjectedItemAsync(
        string threadId,
        string turnId,
        string itemId,
        CancellationToken ct = default) =>
        ReadProjectionAsync(threadId, path => _historyProjectionStore.ReadItemAsync(threadId, path, turnId, itemId, ct), ct);

    internal Task<IReadOnlyList<ThreadHistoryItem>> ReadUnfinishedCommandItemsAsync(
        string threadId,
        CancellationToken ct = default) =>
        ReadProjectionAsync(threadId, path => _historyProjectionStore.ReadUnfinishedCommandItemsAsync(threadId, path, ct), ct);

    internal Task<ThreadHistoryAggregates> ReadHistoryAggregatesAsync(
        string threadId,
        IReadOnlyCollection<string> excludedTurnIds,
        CancellationToken ct = default) =>
        ReadProjectionAsync(
            threadId,
            path => _historyProjectionStore.ReadAggregatesAsync(threadId, path, excludedTurnIds, ct),
            ct);

    internal async Task ReloadWorkingSetAsync(SessionThread thread, CancellationToken ct = default)
    {
        var loaded = await ReadProjectionAsync(
            thread.Id,
            path => _historyProjectionStore.ReadWorkingSetAsync(thread.Id, path, ct),
            ct);
        thread.Turns = loaded.Turns;
        thread.OmittedTurnCount = loaded.OmittedTurnCount;
        thread.OmittedTokenUsage = loaded.OmittedTokenUsage;
    }

    private async Task<T> ReadProjectionAsync<T>(string threadId, Func<string, Task<T>> read, CancellationToken ct)
    {
        using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        return await read(ResolvePersistedHistoryPath(threadId));
    }
}
