using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private static readonly TimeSpan SlowHistoryRead = TimeSpan.FromSeconds(1);

    public async Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(
        string threadId,
        CancellationToken ct = default)
    {
        var snapshot = await ReadHistoryAsync(
            threadId,
            "snapshot",
            (path, repair) => _historyProjectionStore.ReadSnapshotAsync(threadId, path, repair, ct),
            static _ => 1,
            ct);
        if (snapshot.Thread.Ephemeral)
            throw new NotSupportedException("Paged history is not supported for ephemeral Threads.");
        return snapshot;
    }

    public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
        string threadId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default)
    {
        ValidateHistoryPageLimit(limit);
        return ReadHistoryAsync(
            threadId,
            "turns",
            (path, repair) => _historyProjectionStore.ListTurnsAsync(threadId, path, cursor, limit, direction, repair, ct),
            static page => page.Data.Count,
            ct);
    }

    public Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
        string threadId,
        string? turnId,
        ThreadHistoryCursor? cursor,
        int limit,
        ThreadHistorySortDirection direction,
        CancellationToken ct = default)
    {
        ValidateHistoryPageLimit(limit);
        return ReadHistoryAsync(
            threadId,
            turnId is null ? "items" : "turn_items",
            (path, repair) => _historyProjectionStore.ListItemsAsync(threadId, path, turnId, cursor, limit, direction, repair, ct),
            static page => page.Data.Count,
            ct);
    }

    private async Task<T> ReadHistoryAsync<T>(
        string threadId,
        string scope,
        Func<string, bool, Task<T>> read,
        Func<T, int> rows,
        CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var gateWait = TimeSpan.Zero;
        var path = ResolvePersistedHistoryPath(threadId);
        var (served, result) = _historyProjectionStore.IsCurrent(threadId, path)
            ? await TryReadCommittedAsync(path)
            : (false, default!);
        if (!served)
        {
            using var readLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
            gateWait = Stopwatch.GetElapsedTime(started);
            result = await read(ResolvePersistedHistoryPath(threadId), true);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var level = elapsed > SlowHistoryRead ? LogLevel.Warning : LogLevel.Debug;
        if (_logger.IsEnabled(level))
        {
            _logger.Log(
                level,
                "History read {Scope} for thread {ThreadId} returned {Rows} rows in {ElapsedMs} ms, {GateWaitMs} ms waiting for the write gate",
                scope,
                threadId,
                rows(result),
                (long)elapsed.TotalMilliseconds,
                (long)gateWait.TotalMilliseconds);
        }
        return result;

        async Task<(bool Served, T Result)> TryReadCommittedAsync(string currentPath)
        {
            try
            {
                return (true, await read(currentPath, false));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return (false, default!);
            }
        }
    }
}
