using System.Collections.Concurrent;
using DotCraft.Sessions;

namespace DotCraft.Dreams;

/// <summary>The Dreams runs in model work, keyed by their internal Thread, which the Dreams tool profile serves.</summary>
public sealed class DreamsRunRegistry(ThreadStore threadStore)
{
    private readonly ConcurrentDictionary<string, DreamsRunSession> _runs = new(StringComparer.Ordinal);

    public DreamsRunSession Register(string threadId, DreamsRunInput input)
    {
        var session = new DreamsRunSession(input, threadStore);
        _runs[threadId] = session;
        return session;
    }

    public void Unregister(string threadId) => _runs.TryRemove(threadId, out _);

    public bool TryGet(string? threadId, out DreamsRunSession session)
    {
        session = null!;
        return !string.IsNullOrWhiteSpace(threadId) && _runs.TryGetValue(threadId, out session!);
    }
}
