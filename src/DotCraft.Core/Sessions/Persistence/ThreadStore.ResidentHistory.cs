using System.Collections.Concurrent;
using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal readonly record struct ModelHistoryRevision(long Value, long LastFailedWrite, long LastCheckpoint);

internal sealed record ResidentModelHistoryLoad(List<ChatMessage> Messages, ModelHistoryRevision Revision, bool Residable);

public sealed partial class ThreadStore
{
    private readonly ConcurrentDictionary<string, ModelHistoryRevision> _modelHistoryRevisions =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ResidentProviderHistory> _residentProviderHistory =
        new(StringComparer.Ordinal);
    private long _modelHistoryClock;
    private long _rolloutReplayCount;

    internal long RolloutReplayCount => Interlocked.Read(ref _rolloutReplayCount);

    internal ModelHistoryRevision GetModelHistoryRevision(string threadId) =>
        _modelHistoryRevisions.TryGetValue(threadId, out var revision) ? revision : default;

    internal async Task<ResidentModelHistoryLoad> LoadResidentModelHistoryAsync(
        SessionThread thread,
        string? excludedTurnId,
        CancellationToken ct = default)
    {
        var revision = GetModelHistoryRevision(thread.Id);
        var (history, projected) = await RebuildModelHistoryFromRolloutAsync(thread, excludedTurnId, ct);
        return new ResidentModelHistoryLoad(
            history,
            revision,
            !projected && GetModelHistoryRevision(thread.Id) == revision);
    }

    internal static List<ModelHistoryMessage> EncodeModelHistory(IEnumerable<ChatMessage> messages)
    {
        var codec = new ModelHistoryCodec();
        return messages.Select(message => codec.Encode(message)).ToList();
    }

    internal static List<ChatMessage> DecodeModelHistory(IEnumerable<ModelHistoryMessage> messages)
    {
        var codec = new ModelHistoryCodec();
        return MessageGrouper.NormalizeFunctionCallArguments(messages.Select(codec.Decode).ToList());
    }

    internal void ReleaseResidentHistory(string threadId) =>
        _residentProviderHistory.TryRemove(threadId, out _);

    private void InvalidateResidentHistory(string threadId)
    {
        _residentProviderHistory.TryRemove(threadId, out _);
        AdvanceModelHistoryRevision(threadId, failed: true, checkpoint: false);
    }

    private void AdvanceModelHistoryRevision(string threadId, bool failed, bool checkpoint)
    {
        var next = Interlocked.Increment(ref _modelHistoryClock);
        _modelHistoryRevisions.AddOrUpdate(
            threadId,
            _ => new ModelHistoryRevision(next, failed ? next : 0, checkpoint ? next : 0),
            (_, current) => new ModelHistoryRevision(
                next,
                failed ? next : current.LastFailedWrite,
                checkpoint ? next : current.LastCheckpoint));
    }

    private async Task<T> WriteModelHistoryAsync<T>(string threadId, Func<Task<T>> write, bool checkpoint = false)
    {
        T result;
        try
        {
            result = await write();
        }
        catch
        {
            InvalidateResidentHistory(threadId);
            throw;
        }

        AdvanceModelHistoryRevision(threadId, failed: false, checkpoint);
        return result;
    }

    private async Task<RolloutAppendResult> WriteProviderHistoryAsync(
        string threadId,
        Func<Task<RolloutAppendResult>> write)
    {
        RolloutAppendResult result;
        try
        {
            result = await write();
        }
        catch
        {
            _residentProviderHistory.TryRemove(threadId, out _);
            throw;
        }

        if (result.Record != null && _residentProviderHistory.TryGetValue(threadId, out var resident))
            resident.Append(result.Record);
        return result;
    }

    private void RecordResidentCheckpoint(string threadId, DateTimeOffset createdAt)
    {
        if (_residentProviderHistory.TryGetValue(threadId, out var resident))
            resident.RecordCheckpoint(createdAt);
    }

    private async Task<ProviderHistoryInputs> ReadProviderHistoryInputsAsync(
        string threadId,
        bool retain,
        CancellationToken ct)
    {
        if (_residentProviderHistory.TryGetValue(threadId, out var resident))
            return resident.Snapshot();
        if (!retain)
            return await ReadProviderHistoryInputsFromRolloutAsync(threadId, ct);

        using var writeLock = await ThreadRolloutWriteGate.AcquireAsync(_botPath, threadId, ct);
        if (_residentProviderHistory.TryGetValue(threadId, out resident))
            return resident.Snapshot();
        var inputs = await ReadProviderHistoryInputsFromRolloutAsync(threadId, ct);
        _residentProviderHistory[threadId] = new ResidentProviderHistory(inputs);
        return inputs;
    }

    private async Task<ProviderHistoryInputs> ReadProviderHistoryInputsFromRolloutAsync(
        string threadId,
        CancellationToken ct)
    {
        Interlocked.Increment(ref _rolloutReplayCount);
        var records = await _rolloutStore.LoadProviderHistoryRecordsAsync(threadId, ct);
        var checkpoints = await _rolloutStore.LoadCompactionCheckpointsAsync(threadId, ct);
        return new ProviderHistoryInputs(records, checkpoints.LastOrDefault()?.CreatedAt);
    }

    private sealed record ProviderHistoryInputs(
        IReadOnlyList<ThreadRolloutRecord> Records,
        DateTimeOffset? LatestCheckpointAt);

    private sealed class ResidentProviderHistory(ProviderHistoryInputs inputs)
    {
        private readonly object _lock = new();
        private readonly List<ThreadRolloutRecord> _records = [.. inputs.Records];
        private DateTimeOffset? _latestCheckpointAt = inputs.LatestCheckpointAt;

        public void Append(ThreadRolloutRecord record)
        {
            lock (_lock)
                _records.Add(record);
        }

        public void RecordCheckpoint(DateTimeOffset createdAt)
        {
            lock (_lock)
                _latestCheckpointAt = createdAt;
        }

        public ProviderHistoryInputs Snapshot()
        {
            lock (_lock)
                return new ProviderHistoryInputs(_records.ToArray(), _latestCheckpointAt);
        }
    }
}
