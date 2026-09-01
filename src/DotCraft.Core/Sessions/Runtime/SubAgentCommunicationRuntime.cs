using System.Collections.Concurrent;

namespace DotCraft.Sessions;

internal enum SubAgentCommunicationActivityKind
{
    Mailbox,
    Graph,
    Steer
}

internal interface ISubAgentCommunicationRuntimeProvider
{
    SubAgentCommunicationRuntime CommunicationRuntime { get; }
}

internal sealed class SubAgentCommunicationRuntime
{
    private readonly record struct InboxKey(string RootThreadId, string TargetAgentPath);

    private sealed class ActivitySubscription(
        SubAgentCommunicationRuntime owner,
        string rootThreadId,
        string targetAgentPath,
        bool inputOnly) : IDisposable
    {
        private int _disposed;

        public TaskCompletionSource Signal { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string RootThreadId { get; } = rootThreadId;

        public string TargetAgentPath { get; } = targetAgentPath;

        public bool InputOnly { get; } = inputOnly;

        public Task Activity => Signal.Task;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Remove(this);
        }
    }

    private sealed class InboxLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                gate.Release();
        }
    }

    private readonly ConcurrentDictionary<InboxKey, SemaphoreSlim> _inboxLocks = new();
    private readonly Lock _subscriptionsLock = new();
    private readonly HashSet<ActivitySubscription> _subscriptions = [];

    public static SubAgentCommunicationRuntime For(ISessionService sessionService) =>
        sessionService is ISubAgentCommunicationRuntimeProvider provider
            ? provider.CommunicationRuntime
            : throw new InvalidOperationException(
                $"Session service '{sessionService.GetType().Name}' does not provide a SubAgent communication runtime.");

    public async Task<IDisposable> AcquireInboxAsync(
        string rootThreadId,
        string targetAgentPath,
        CancellationToken ct)
    {
        var gate = _inboxLocks.GetOrAdd(
            new InboxKey(rootThreadId, targetAgentPath),
            static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        return new InboxLease(gate);
    }

    public IDisposable Subscribe(
        string rootThreadId,
        string targetAgentPath,
        out Task activity)
    {
        var subscription = new ActivitySubscription(this, rootThreadId, targetAgentPath, inputOnly: false);
        lock (_subscriptionsLock)
            _subscriptions.Add(subscription);
        activity = subscription.Activity;
        return subscription;
    }

    public IDisposable SubscribeInput(
        string rootThreadId,
        string targetAgentPath,
        out Task activity)
    {
        var subscription = new ActivitySubscription(this, rootThreadId, targetAgentPath, inputOnly: true);
        lock (_subscriptionsLock)
            _subscriptions.Add(subscription);
        activity = subscription.Activity;
        return subscription;
    }

    public void PublishMailbox(string rootThreadId, string targetAgentPath) =>
        Publish(rootThreadId, targetAgentPath, SubAgentCommunicationActivityKind.Mailbox);

    public void PublishGraph(string rootThreadId) =>
        Publish(rootThreadId, targetAgentPath: null, SubAgentCommunicationActivityKind.Graph);

    public void PublishSteer(string rootThreadId, string targetAgentPath) =>
        Publish(rootThreadId, targetAgentPath, SubAgentCommunicationActivityKind.Steer);

    private void Publish(
        string rootThreadId,
        string? targetAgentPath,
        SubAgentCommunicationActivityKind kind)
    {
        ActivitySubscription[] matches;
        lock (_subscriptionsLock)
        {
            matches = _subscriptions
                .Where(subscription =>
                    string.Equals(subscription.RootThreadId, rootThreadId, StringComparison.Ordinal)
                    && (!subscription.InputOnly || kind is SubAgentCommunicationActivityKind.Mailbox or SubAgentCommunicationActivityKind.Steer)
                    && (kind == SubAgentCommunicationActivityKind.Graph
                        || string.Equals(subscription.TargetAgentPath, targetAgentPath, StringComparison.Ordinal)))
                .ToArray();
        }

        foreach (var subscription in matches)
            subscription.Signal.TrySetResult();
    }

    private void Remove(ActivitySubscription subscription)
    {
        lock (_subscriptionsLock)
            _subscriptions.Remove(subscription);
    }
}
