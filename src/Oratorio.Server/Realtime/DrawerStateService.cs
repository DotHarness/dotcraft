using System.Collections.Concurrent;
using System.Text.Json;
using Oratorio.Server.Api;

namespace Oratorio.Server.Realtime;

/// <summary>
/// Keeps short-lived drawer snapshots for active AppServer-backed runs.
/// </summary>
public sealed class DrawerStateService
{
    private const int ConversationCapacity = 200;
    private readonly ConcurrentDictionary<string, DrawerRunState> _states = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _evictions = new(StringComparer.Ordinal);

    public DrawerSnapshotResponse GetSnapshot(string runId)
    {
        if (!_states.TryGetValue(runId, out var state))
        {
            return new DrawerSnapshotResponse(runId, [], null, null, null, FromCache: false);
        }

        return state.ToSnapshot(runId, fromCache: true);
    }

    public bool HasSnapshot(string runId) =>
        _states.ContainsKey(runId);

    public ConversationItemDto UpsertItem(string runId, ConversationItemDto item, DateTimeOffset timestamp)
    {
        CancelEviction(runId);
        return GetOrCreate(runId).Upsert(item, timestamp);
    }

    public ConversationItemDto AppendDelta(
        string runId,
        string itemId,
        string? turnId,
        string itemType,
        string deltaKind,
        string delta,
        DateTimeOffset timestamp)
    {
        CancelEviction(runId);
        return GetOrCreate(runId).AppendDelta(itemId, turnId, itemType, deltaKind, delta, timestamp);
    }

    public void ReplacePlan(string runId, PlanSnapshotDto plan, DateTimeOffset timestamp)
    {
        CancelEviction(runId);
        GetOrCreate(runId).ReplacePlan(plan, timestamp);
    }

    public void UpdateRunStatus(string runId, RunSummaryDto run, DateTimeOffset timestamp)
    {
        CancelEviction(runId);
        GetOrCreate(runId).UpdateRunStatus(run, timestamp);
    }

    public void ScheduleEviction(string runId, TimeSpan delay)
    {
        CancelEviction(runId);
        var cts = new CancellationTokenSource();
        _evictions[runId] = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                _states.TryRemove(runId, out _);
                _evictions.TryRemove(runId, out _);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        });
    }

    public void Evict(string runId)
    {
        CancelEviction(runId);
        _states.TryRemove(runId, out _);
    }

    private DrawerRunState GetOrCreate(string runId) =>
        _states.GetOrAdd(runId, _ => new DrawerRunState());

    private void CancelEviction(string runId)
    {
        if (_evictions.TryRemove(runId, out var cts))
        {
            cts.Cancel();
        }
    }

    private sealed class DrawerRunState
    {
        private readonly object _gate = new();
        private readonly Queue<string> _order = new();
        private readonly Dictionary<string, ConversationItemDto> _items = new(StringComparer.Ordinal);
        private PlanSnapshotDto? _plan;
        private RunSummaryDto? _run;
        private DateTimeOffset? _watermark;

        public DrawerSnapshotResponse ToSnapshot(string runId, bool fromCache)
        {
            lock (_gate)
            {
                return new DrawerSnapshotResponse(
                    runId,
                    _order.Where(_items.ContainsKey).Select(id => _items[id]).ToArray(),
                    _plan,
                    _run,
                    _watermark,
                    fromCache);
            }
        }

        public ConversationItemDto Upsert(ConversationItemDto item, DateTimeOffset timestamp)
        {
            lock (_gate)
            {
                var normalized = item with { Payload = DrawerPayloadSanitizer.SafeClonePayload(item.Type, item.Payload, out _) };
                if (!_items.ContainsKey(normalized.Id))
                {
                    _order.Enqueue(normalized.Id);
                }

                _items[normalized.Id] = normalized;
                Trim();
                _watermark = Max(_watermark, timestamp);
                return normalized;
            }
        }

        public ConversationItemDto AppendDelta(
            string itemId,
            string? turnId,
            string itemType,
            string deltaKind,
            string delta,
            DateTimeOffset timestamp)
        {
            lock (_gate)
            {
                if (!_items.TryGetValue(itemId, out var existing))
                {
                    existing = new ConversationItemDto(
                        itemId,
                        turnId,
                        itemType,
                        "started",
                        DrawerPayloadSanitizer.CreateDeltaPayload(itemType, deltaKind, delta),
                        timestamp,
                        null,
                        Streaming: true);
                    _order.Enqueue(itemId);
                }
                else
                {
                    existing = existing with
                    {
                        Payload = DrawerPayloadSanitizer.MergeDeltaPayload(existing.Type, deltaKind, existing.Payload, delta, out _),
                        Streaming = true
                    };
                }

                _items[itemId] = existing;
                Trim();
                _watermark = Max(_watermark, timestamp);
                return existing;
            }
        }

        public void ReplacePlan(PlanSnapshotDto plan, DateTimeOffset timestamp)
        {
            lock (_gate)
            {
                _plan = plan;
                _watermark = Max(_watermark, timestamp);
            }
        }

        public void UpdateRunStatus(RunSummaryDto run, DateTimeOffset timestamp)
        {
            lock (_gate)
            {
                _run = run;
                _watermark = Max(_watermark, timestamp);
            }
        }

        private void Trim()
        {
            while (_items.Count > ConversationCapacity && _order.TryDequeue(out var id))
            {
                _items.Remove(id);
            }
        }

        private static string? ExtractString(JsonElement element, string propertyName) =>
            element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;

        private static DateTimeOffset Max(DateTimeOffset? current, DateTimeOffset candidate) =>
            current is null || candidate > current ? candidate : current.Value;
    }
}
