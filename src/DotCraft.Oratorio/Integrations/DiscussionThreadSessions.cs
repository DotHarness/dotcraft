using System.Collections.Concurrent;

namespace DotCraft.Oratorio.Integrations;

public sealed class DiscussionThreadSession(IDotCraftAppServerClient client, string threadId)
{
    private readonly object _turnGate = new();
    private readonly HashSet<string> _endedTurns = new(StringComparer.Ordinal);
    private string? _activeTurnId;

    public IDotCraftAppServerClient Client { get; } = client;
    public string ThreadId { get; } = threadId;
    public SemaphoreSlim DeliveryGate { get; } = new(1, 1);
    public ConcurrentDictionary<string, DateTimeOffset> Waiting { get; } = new(StringComparer.Ordinal);
    public bool Closed { get; set; }

    public string? ActiveTurnId
    {
        get
        {
            lock (_turnGate)
            {
                return _activeTurnId;
            }
        }
    }

    public void TurnStarted(string? turnId)
    {
        lock (_turnGate)
        {
            if (!string.IsNullOrWhiteSpace(turnId) && !_endedTurns.Contains(turnId))
            {
                _activeTurnId = turnId;
            }
        }
    }

    public void TurnEnded(string? turnId)
    {
        lock (_turnGate)
        {
            if (!string.IsNullOrWhiteSpace(turnId))
            {
                _endedTurns.Add(turnId);
            }

            if (string.IsNullOrWhiteSpace(turnId) || string.Equals(_activeTurnId, turnId, StringComparison.Ordinal))
            {
                _activeTurnId = null;
            }
        }
    }
}

public sealed class DiscussionThreadSessions
{
    private readonly ConcurrentDictionary<string, DiscussionThreadSession> _sessions = new(StringComparer.Ordinal);

    public DiscussionThreadSession? Find(string threadId) =>
        _sessions.TryGetValue(threadId, out var session) && !session.Closed ? session : null;

    public void Add(DiscussionThreadSession session) => _sessions[session.ThreadId] = session;

    public void Remove(DiscussionThreadSession session) =>
        _sessions.TryRemove(KeyValuePair.Create(session.ThreadId, session));

    public bool Holds(string discussionTurnId) =>
        _sessions.Values.Any(session => session.Waiting.ContainsKey(discussionTurnId));

    public async Task WaitForIdleTurnAsync(string threadId, CancellationToken ct)
    {
        while (Find(threadId)?.ActiveTurnId is not null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }
}
