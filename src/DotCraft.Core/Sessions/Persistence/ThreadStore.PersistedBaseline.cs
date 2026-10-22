using System.Collections.Concurrent;
using System.Text.Json;

namespace DotCraft.Sessions;

public sealed partial class ThreadStore
{
    private readonly ConcurrentDictionary<string, PersistedBaseline> _persistedBaselines =
        new(StringComparer.Ordinal);

    private async Task<RolloutAppendResult> SaveThreadUnderGateAsync(SessionThread thread, CancellationToken ct)
    {
        var previous = await ResolvePersistedBaselineAsync(thread, ct);
        return await TrackPersistedAsync(
            thread.Id,
            () => _rolloutStore.SaveThreadAsync(thread, previous, ct),
            thread.Turns);
    }

    private async Task<SessionThread?> ResolvePersistedBaselineAsync(SessionThread current, CancellationToken ct)
    {
        var path = _rolloutStore.ResolveExistingPath(current.Id);
        if (path == null)
        {
            _persistedBaselines.TryRemove(current.Id, out _);
            return null;
        }

        var length = new FileInfo(path).Length;
        if (_persistedBaselines.TryGetValue(current.Id, out var baseline) && baseline.Offset == length)
            return baseline.Replay.Build();

        ThreadReplay replay;
        try
        {
            var (thread, turns) = await _historyProjectionStore.ReadPersistedStateAsync(
                current.Id,
                path,
                current.Turns.Select(static turn => turn.Id).ToList(),
                ct);
            replay = ThreadReplay.FromProjection(thread, turns);
        }
        catch (Exception ex) when (ex is ThreadHistoryUnavailableException
                                       or Microsoft.Data.Sqlite.SqliteException
                                       or InvalidOperationException
                                       or JsonException)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Thread projection unavailable for '{current.Id}'; reading its rollout to diff the save: {ex.Message}");
            Interlocked.Increment(ref _rolloutReplayCount);
            replay = new ThreadReplay();
            await Utf8JsonlReader.ReadAsync(path, replay.Apply, ct);
        }

        _persistedBaselines[current.Id] = new PersistedBaseline(replay, length);
        return replay.Build();
    }

    private async Task<RolloutAppendResult> TrackPersistedAsync(
        string threadId,
        Func<Task<RolloutAppendResult>> write,
        IEnumerable<SessionTurn>? heldTurns = null) =>
        (await TrackPersistedOrSkipAsync(threadId, async () => (RolloutAppendResult?)await write(), heldTurns))!;

    private async Task<RolloutAppendResult?> TrackPersistedOrSkipAsync(
        string threadId,
        Func<Task<RolloutAppendResult?>> write,
        IEnumerable<SessionTurn>? heldTurns = null)
    {
        var before = _rolloutStore.ResolveExistingPath(threadId) is { } path
            ? new FileInfo(path).Length
            : (long?)null;
        if (_persistedBaselines.TryGetValue(threadId, out var current) && current.Offset != before)
            _persistedBaselines.TryRemove(threadId, out _);

        RolloutAppendResult? result;
        try
        {
            result = await write();
        }
        catch
        {
            _persistedBaselines.TryRemove(threadId, out _);
            throw;
        }

        if (result == null)
            return null;
        if (!_persistedBaselines.TryGetValue(threadId, out var baseline))
        {
            if (before != null)
                return result;
            baseline = new PersistedBaseline(new ThreadReplay(), 0);
            _persistedBaselines[threadId] = baseline;
        }

        try
        {
            foreach (var record in result.Records.Where(static record => RolloutKinds.Domain.Contains(record.Kind)))
                baseline.Replay.Apply(JsonSerializer.SerializeToUtf8Bytes(record, SessionJsonOptions.Default));
            if (heldTurns != null)
                baseline.Replay.RetainTurns(heldTurns);
            baseline.Offset = result.Receipt.ConfirmedOffset;
        }
        catch (InvalidDataException)
        {
            _persistedBaselines.TryRemove(threadId, out _);
        }

        return result;
    }

    private async Task<SessionThread?> ReadThreadRecordingBaselineAsync(string path, CancellationToken ct)
    {
        var length = new FileInfo(path).Length;
        var baseline = new ThreadReplay();
        Interlocked.Increment(ref _rolloutReplayCount);
        var thread = await ThreadRolloutStore.ReadThreadFileAsync(path, ct, baseline);
        if (thread != null
            && _rolloutStore.ResolveExistingPath(thread.Id) is { } canonicalPath
            && string.Equals(Path.GetFullPath(canonicalPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            _persistedBaselines[thread.Id] = new PersistedBaseline(baseline, length);
        }

        return thread;
    }

    private sealed class PersistedBaseline(ThreadReplay replay, long offset)
    {
        public ThreadReplay Replay { get; } = replay;

        public long Offset { get; set; } = offset;
    }
}
