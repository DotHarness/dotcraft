namespace DotCraft.RemoteTools;

/// <summary>
/// Records which background terminal sessions each workspace lease created, so stdin can only
/// reach a process that the same lease started through an approved Exec.
/// </summary>
internal sealed class LeaseTerminalRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _leaseBySession = new(StringComparer.Ordinal);

    public void Bind(string leaseId, IEnumerable<string> sessionIds)
    {
        lock (_gate)
        {
            foreach (var sessionId in sessionIds)
                _leaseBySession[sessionId] = leaseId;
        }
    }

    public bool IsBound(string leaseId, string sessionId)
    {
        lock (_gate)
            return _leaseBySession.TryGetValue(sessionId, out var owner)
                   && string.Equals(owner, leaseId, StringComparison.Ordinal);
    }

    public void ReleaseLease(string leaseId)
    {
        lock (_gate)
        {
            foreach (var sessionId in _leaseBySession
                         .Where(pair => string.Equals(pair.Value, leaseId, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _leaseBySession.Remove(sessionId);
            }
        }
    }
}
