using System.Collections.Concurrent;
using DotCraft.Sessions;
using DotCraft.Tools;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public partial class TestableSessionService
{
    private readonly ConcurrentDictionary<string, ThreadEventBroker> _brokers = new(StringComparer.Ordinal);

    private ThreadEventBroker GetBroker(string threadId) => _brokers.GetOrAdd(threadId, id =>
    {
        var broker = new ThreadEventBroker(id);
        if (_cache.TryGetValue(id, out var thread))
            broker.Initialize(thread, 1);
        return broker;
    });

    public Task<ThreadObservation> ObserveThreadAsync(
        string threadId,
        ThreadObservationView view = ThreadObservationView.Metadata,
        IReadOnlyList<ToolName>? summaryTools = null,
        CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(threadId, out var thread))
            throw new InvalidOperationException("Thread is not loaded.");
        return Task.FromResult(GetBroker(threadId).Observe(thread, null, view, summaryTools ?? [], ct));
    }

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, CancellationToken ct = default) =>
        GetBroker(threadId).SubscribeAsync(ct);

    public Task WaitForThreadSubscriberAsync(string threadId, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!GetBroker(threadId).HasSubscribers)
            throw new InvalidOperationException($"No thread observation for '{threadId}'.");
        return Task.CompletedTask;
    }

    public void PublishThreadEvent(SessionEvent evt)
    {
        var broker = GetBroker(evt.ThreadId);
        var channel = broker.CreateTurnChannel(evt.TurnId ?? string.Empty);
        switch (evt.EventType)
        {
            case SessionEventType.TurnStarted when evt.TurnPayload is { } turn: channel.EmitTurnStarted(turn); break;
            case SessionEventType.TurnCompleted when evt.TurnPayload is { } turn: channel.EmitTurnCompleted(turn); break;
            case SessionEventType.TurnFailed when evt.TurnPayload is { } turn: channel.EmitTurnFailed(turn, evt.TurnFailedPayload!.Error); break;
            case SessionEventType.TurnCancelled when evt.TurnPayload is { } turn: channel.EmitTurnCancelled(turn, evt.TurnCancelledPayload!.Reason); break;
            case SessionEventType.ItemStarted when evt.ItemPayload is { } item: channel.EmitItemStarted(item); break;
            case SessionEventType.ItemCompleted when evt.ItemPayload is { } item: channel.EmitItemCompleted(item); break;
            case SessionEventType.ApprovalRequested when evt.ItemPayload is { } item: channel.EmitApprovalRequested(item); break;
            case SessionEventType.ApprovalResolved when evt.ItemPayload is { } item: channel.EmitApprovalResolved(item); break;
            case SessionEventType.UserInputRequested when evt.ItemPayload is { } item: channel.EmitUserInputRequested(item); break;
            case SessionEventType.UserInputResolved when evt.ItemPayload is { } item: channel.EmitUserInputResolved(item); break;
            case SessionEventType.SubAgentProgress: channel.EmitSubAgentProgress(evt.SubAgentProgressPayload!); break;
            case SessionEventType.ItemDelta: broker.PublishItemDelta(evt.TurnId!, evt.ItemId!, evt.Payload!); break;
            case SessionEventType.TurnDiffUpdated: channel.EmitTurnDiffUpdated(evt.TurnDiffUpdatedPayload!.Diff); break;
            default: broker.PublishThreadEvent(evt.EventType, evt.Payload!); break;
        }
    }

    private async IAsyncEnumerable<SessionEvent> BroadcastSubmitEvents(
        IAsyncEnumerable<SessionEvent> events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in events.WithCancellation(ct))
        {
            PublishThreadEvent(evt);
            yield return evt;
        }
    }

    public Task<ThreadHistoryCursor?> ResolveThreadTurnCursorAsync(
        string threadId, string turnId, CancellationToken ct = default) =>
        _store.ResolveThreadTurnCursorAsync(threadId, turnId, ct);

    public Task<ThreadHistoryCursor?> ResolveThreadItemCursorAsync(
        string threadId, string turnId, string itemId, CancellationToken ct = default) =>
        _store.ResolveThreadItemCursorAsync(threadId, turnId, itemId, ct);

    public Task<ThreadHistoryPage<ThreadTurnSummary>> ListThreadSummariesAsync(
        string threadId, ThreadHistoryCursor? cursor, int limit, ThreadHistorySortDirection direction,
        IReadOnlyList<ToolName> summaryTools, CancellationToken ct = default) =>
        _store.ListThreadSummariesAsync(threadId, cursor, limit, direction, summaryTools, ct);
}