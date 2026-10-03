using System.Runtime.CompilerServices;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class AppServerInteractiveRequestHolders
{
    private const int ResolvedHistoryLimit = 512;

    private static readonly ConditionalWeakTable<ISessionService, AppServerInteractiveRequestHolders> Shared = new();

    private readonly Lock _gate = new();
    private readonly Dictionary<RequestKey, Entry> _open = [];
    private readonly HashSet<RequestKey> _resolved = [];
    private readonly Queue<RequestKey> _resolvedOrder = new();

    public static AppServerInteractiveRequestHolders For(ISessionService sessionService) =>
        Shared.GetValue(sessionService, static _ => new AppServerInteractiveRequestHolders());

    public Hold? TryHold(RequestKey key)
    {
        lock (_gate)
        {
            if (_resolved.Contains(key))
                return null;
            if (!_open.TryGetValue(key, out var entry))
                _open[key] = entry = new Entry();
            entry.Holders++;
            return new Hold(key, entry);
        }
    }

    public bool TryResolve(RequestKey key)
    {
        Entry? entry;
        lock (_gate)
        {
            if (!_resolved.Add(key))
                return false;
            _resolvedOrder.Enqueue(key);
            if (_resolvedOrder.Count > ResolvedHistoryLimit)
                _resolved.Remove(_resolvedOrder.Dequeue());
            _open.Remove(key, out entry);
        }

        entry?.Resolved.TrySetResult();
        return true;
    }

    public void Release(Hold hold)
    {
        lock (_gate)
        {
            if (--hold.Entry.Holders == 0
                && _open.TryGetValue(hold.Key, out var entry)
                && ReferenceEquals(entry, hold.Entry))
            {
                _open.Remove(hold.Key);
            }
        }
    }

    internal sealed record Hold(RequestKey Key, Entry Entry)
    {
        public Task Resolved => Entry.Resolved.Task;
    }

    internal sealed class Entry
    {
        public int Holders { get; set; }

        public TaskCompletionSource Resolved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal readonly record struct RequestKey(string Method, string ThreadId, string TurnId, string RequestId);
}
