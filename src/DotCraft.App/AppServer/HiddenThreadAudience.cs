using System.Collections.Concurrent;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

internal sealed class HiddenThreadAudience(Func<string, bool> isLoadedThreadHidden)
{
    private readonly ConcurrentDictionary<string, byte> _hidden = new(StringComparer.Ordinal);

    public void Observe(SessionThread thread)
    {
        if (ThreadVisibility.IsHidden(thread))
            _hidden.TryAdd(thread.Id, 0);
        else
            _hidden.TryRemove(thread.Id, out _);
    }

    public bool Accepts(string threadId, AppServerConnection connection) =>
        !IsHidden(threadId) || connection.HasSubscription(threadId);

    public void Forget(string threadId) => _hidden.TryRemove(threadId, out _);

    private bool IsHidden(string threadId)
    {
        if (_hidden.ContainsKey(threadId))
            return true;
        if (!isLoadedThreadHidden(threadId))
            return false;
        _hidden.TryAdd(threadId, 0);
        return true;
    }
}
