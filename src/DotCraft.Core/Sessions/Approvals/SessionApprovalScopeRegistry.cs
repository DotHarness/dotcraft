using System.Collections.Concurrent;

namespace DotCraft.Sessions;

/// <summary>
/// Holds approval scopes accepted for the lifetime of a Session thread.
/// </summary>
public sealed class SessionApprovalScopeRegistry
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _scopes =
        new(StringComparer.Ordinal);

    public bool Contains(string threadId, string scope) =>
        _scopes.TryGetValue(threadId, out var scopes) && scopes.ContainsKey(scope);

    public void Add(string threadId, string scope) =>
        _scopes.GetOrAdd(threadId, static _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal))
            .TryAdd(scope, 0);

    public void RemoveThread(string threadId) => _scopes.TryRemove(threadId, out _);
}
