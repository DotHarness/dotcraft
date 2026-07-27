using DotCraft.Protocol.AppServer;

namespace DotCraft.Protocol;

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

        public async Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct)
        {
            var thread = await owner.GetOrLoadThreadAsync(threadId, ct);
            await owner.EnsurePerThreadAgentIfMissingAsync(threadId, thread, ct);
            return thread;
        }
    }
}
