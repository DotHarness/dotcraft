using DotCraft.AppServer;
using System.Text.Json;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private sealed class ThreadAccessCoordinator(SessionService owner)
    {
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
                return null;

            var snapshot = owner.Persistence.LoadContextUsageSnapshot(threadId);
            return snapshot is null
                ? null
                : owner.CreateContextUsageSnapshot(
                    threadId,
                    snapshot.Tokens,
                    snapshot.Source,
                    snapshot.IsEstimate);
        }

        public ThreadSummaryRuntime GetRuntimeSnapshot(SessionThread thread)
        {
            var maintenanceKind = owner._runtimeRegistry.TryGetRuntime(thread.Id, out var runtime)
                ? runtime.Maintenance?.Kind
                : null;
            return ThreadSummaryRuntime.FromThread(thread, maintenanceKind);
        }

        public IReadOnlyDictionary<string, string> GetItemWidgetStates(string threadId) =>
            owner.Persistence.GetItemWidgetStates(NormalizeRequiredThreadId(threadId));

        public void SetItemWidgetState(string threadId, string callId, string? widgetStateJson)
        {
            var normalizedThreadId = NormalizeRequiredThreadId(threadId);
            if (string.IsNullOrWhiteSpace(callId))
                throw AppServerErrors.InvalidParams("'callId' is required.");

            if (string.IsNullOrWhiteSpace(widgetStateJson))
                owner.Persistence.DeleteItemWidgetState(normalizedThreadId, callId);
            else
                owner.Persistence.SaveItemWidgetState(normalizedThreadId, callId, widgetStateJson);
        }

        public async IAsyncEnumerable<SessionEvent> Subscribe(
            string threadId,
            bool replayRecent,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await owner.GetOrLoadThreadAsync(threadId, ct);
            var broker = owner.GetOrCreateBroker(threadId);
            await foreach (var evt in broker.SubscribeAsync(replayRecent, ct).WithCancellation(ct))
                yield return evt;
        }

        public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct) =>
            await owner.GetOrLoadThreadAsync(threadId, ct);

        public async Task<ThreadHistorySnapshot> ReadThreadSnapshotAsync(string threadId, CancellationToken ct)
        {
            var normalized = NormalizeRequiredThreadId(threadId);
            if (owner._runtimeRegistry.TryGetThread(normalized, out var ephemeral) && ephemeral.Ephemeral)
            {
                var header = JsonSerializer.Deserialize<SessionThread>(
                    JsonSerializer.Serialize(ephemeral, SessionJsonOptions.Default),
                    SessionJsonOptions.Default)!;
                header.Turns = [];
                return new ThreadHistorySnapshot(header, GetRuntimeSnapshot(ephemeral));
            }
            var snapshot = await owner.Persistence.ReadThreadSnapshotAsync(normalized, ct);
            if (!owner._runtimeRegistry.TryGetThread(normalized, out var loaded))
                return snapshot;
            return snapshot with { PersistedRuntime = GetRuntimeSnapshot(loaded) };
        }

        public Task<ThreadHistoryPage<SessionTurn>> ListThreadTurnsAsync(
            string threadId,
            ThreadHistoryCursor? cursor,
            int limit,
            ThreadHistorySortDirection direction,
            CancellationToken ct) =>
            ListTurnsCoreAsync(NormalizeRequiredThreadId(threadId), cursor, limit, direction, ct);

        public Task<ThreadHistoryPage<ThreadHistoryItem>> ListThreadItemsAsync(
            string threadId,
            string? turnId,
            ThreadHistoryCursor? cursor,
            int limit,
            ThreadHistorySortDirection direction,
            CancellationToken ct) =>
            ListItemsCoreAsync(NormalizeRequiredThreadId(threadId), turnId, cursor, limit, direction, ct);

        private Task<ThreadHistoryPage<SessionTurn>> ListTurnsCoreAsync(
            string threadId,
            ThreadHistoryCursor? cursor,
            int limit,
            ThreadHistorySortDirection direction,
            CancellationToken ct)
        {
            ThrowIfEphemeral(threadId);
            return owner.Persistence.ListThreadTurnsAsync(threadId, cursor, limit, direction, ct);
        }

        private Task<ThreadHistoryPage<ThreadHistoryItem>> ListItemsCoreAsync(
            string threadId,
            string? turnId,
            ThreadHistoryCursor? cursor,
            int limit,
            ThreadHistorySortDirection direction,
            CancellationToken ct)
        {
            ThrowIfEphemeral(threadId);
            return owner.Persistence.ListThreadItemsAsync(threadId, turnId, cursor, limit, direction, ct);
        }

        private void ThrowIfEphemeral(string threadId)
        {
            if (owner._runtimeRegistry.TryGetThread(threadId, out var thread) && thread.Ephemeral)
                throw new NotSupportedException("Paged history is not supported for ephemeral Threads.");
        }

        public async Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            await owner.EnsurePerThreadAgentIfMissingAsync(threadId, thread, ct);
            return thread;
        }
    }
}
