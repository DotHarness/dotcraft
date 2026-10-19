using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Runtime;

internal sealed class PluginProviderHistoryFactory(
    IProviderHistorySessionFactory target,
    PluginInvocation invocation) : IProviderHistorySessionFactory
{
    private readonly PluginTarget<IProviderHistorySessionFactory> _target = invocation.Capture(target);

    public IProviderConversationHistory CreateSession(
        ProviderConversationIdentity identity, OpaqueProviderHistorySnapshot snapshot,
        IReadOnlyList<ChatMessage> coveredMessages, IProviderHistorySink? sink) =>
        invocation.Invoke<IProviderConversationHistory>(() =>
        {
            var history = _target.Value.CreateSession(identity, snapshot, coveredMessages, sink is null ? null : new Sink(sink));
            return history is IProviderCompactionBridge bridge
                ? new CompactingHistory(history, bridge, invocation) : new History(history, invocation);
        });

    private sealed class Sink(IProviderHistorySink inner) : IProviderHistorySink
    {
        public ValueTask AppendAsync(ProviderHistoryIdentity identity, ProviderHistoryEntrySource source, string? attemptId,
            IReadOnlyList<ProviderHistoryItem> items, CancellationToken ct) =>
            inner.AppendAsync(identity, source, attemptId, CopyItems(items), ct);
        public ValueTask ReplaceAsync(ProviderHistoryIdentity identity, string? coveredThroughTurnId, string reason,
            IReadOnlyList<ProviderHistoryItem> items, CancellationToken ct) =>
            inner.ReplaceAsync(identity, coveredThroughTurnId, reason, CopyItems(items), ct);
        public ValueTask AbortAttemptAsync(ProviderHistoryIdentity identity, string attemptId, CancellationToken ct) =>
            inner.AbortAttemptAsync(identity, attemptId, ct);
    }

    private static ProviderHistoryItem[] CopyItems(IReadOnlyList<ProviderHistoryItem> items) =>
        items.Select(item => new ProviderHistoryItem(item.EntryId, item.Payload.Clone())).ToArray();

    private class History(IProviderConversationHistory target, PluginInvocation invocation) : IProviderConversationHistory
    {
        protected PluginInvocation Invocation { get; } = invocation;
        private readonly PluginTarget<IProviderConversationHistory> _target = invocation.Capture(target, ownsTarget: true);

        public ValueTask AppendLocalInputAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct) =>
            CallAsync(token => _target.Value.AppendLocalInputAsync(messages, token), ct);

        public ValueTask HistoryReplacedAsync(IReadOnlyList<ChatMessage> messages, ChatOptions? options,
            string reason, CancellationToken ct, string? coveredThroughTurnId = null) =>
            CallAsync(token => _target.Value.HistoryReplacedAsync(messages, options, reason, token, coveredThroughTurnId), ct);

        public void MarkProjectionCovered(IReadOnlyList<ChatMessage> messages) =>
            Invocation.Invoke(() => _target.Value.MarkProjectionCovered(messages));
        public string? BeginAttempt() => Invocation.Invoke(() => _target.Value.BeginAttempt());
        public ValueTask AbortAttemptAsync(string? attemptId, CancellationToken ct) =>
            CallAsync(token => _target.Value.AbortAttemptAsync(attemptId, token), ct);
        public void EndAttempt(string? attemptId) => Invocation.Invoke(() => _target.Value.EndAttempt(attemptId));

        public OpaqueProviderHistorySnapshot CaptureOpaqueSnapshot() => Invocation.Invoke(() =>
        {
            var raw = _target.Value.CaptureOpaqueSnapshot();
            var snapshot = raw with { Items = CopyItems(raw.Items) };
            PluginObjectGraphGuard.EnsureHostOwnedGraph(snapshot, "native history snapshot");
            return snapshot;
        });

        public bool TryEstimateActiveContextTokens(IReadOnlyList<ChatMessage> messages, ChatOptions? options, out long tokens)
        {
            var result = Invocation.Invoke(() =>
            {
                var estimated = _target.Value.TryEstimateActiveContextTokens(messages, options, out var value);
                return (estimated, value);
            });
            tokens = result.value;
            return result.estimated;
        }

        private ValueTask CallAsync(Func<CancellationToken, ValueTask> call, CancellationToken ct) =>
            Invocation.InvokeAsync(new Func<ValueTask>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Invocation.Stopping);
                await call(linked.Token).ConfigureAwait(false);
            }));
    }

    private sealed class CompactingHistory(IProviderConversationHistory history, IProviderCompactionBridge bridge,
        PluginInvocation invocation) : History(history, invocation), IProviderCompactionBridge
    {
        private readonly PluginTarget<IProviderCompactionBridge> _bridge = invocation.Capture(bridge);
        public ValueTask<ProviderNativeCompactionInput> CaptureInputAsync(ProviderCompactionPhase phase,
            IReadOnlyList<ChatMessage> messages, ChatOptions? options, CancellationToken ct) =>
            Invocation.InvokeAsync(new Func<ValueTask<ProviderNativeCompactionInput>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Invocation.Stopping);
                return PluginProviderCompactorFactory.Copy(await _bridge.Value.CaptureInputAsync(phase, messages, options, linked.Token).ConfigureAwait(false));
            }));
        public ValueTask ReplaceAsync(ProviderNativeCompactionReplacement replacement, CancellationToken ct) =>
            Invocation.InvokeAsync(new Func<ValueTask>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Invocation.Stopping);
                await _bridge.Value.ReplaceAsync(replacement, linked.Token).ConfigureAwait(false);
            }));
        public long EstimateContextTokens(ProviderNativeCompactionInput snapshot, IReadOnlyList<ChatMessage> pendingTail, ChatOptions? options) =>
            Invocation.Invoke(() => _bridge.Value.EstimateContextTokens(snapshot, pendingTail, options));
    }
}
