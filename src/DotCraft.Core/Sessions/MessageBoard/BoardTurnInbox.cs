namespace DotCraft.Sessions;

internal sealed class BoardTurnInbox(string turnId, CancellationToken cancellationToken) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly Queue<SubAgentMailboxEntry> _pending = new();
    private bool _accepting = true;
    private bool _disposed;

    public string TurnId { get; } = turnId;
    public bool HasPending { get { lock (_gate) return !_disposed && _pending.Count > 0; } }

    public bool TryAdd(SubAgentMailboxEntry notice)
    {
        lock (_gate)
        {
            if (_disposed || !_accepting || cancellationToken.IsCancellationRequested) return false;
            if (_pending.Count >= 256) _pending.Dequeue();
            _pending.Enqueue(notice);
            return true;
        }
    }

    public void SetAdmission(bool accepting)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _accepting = accepting;
            if (!accepting) _pending.Clear();
        }
    }

    public SubAgentMailboxEntry[] Drain()
    {
        lock (_gate)
        {
            if (_disposed || cancellationToken.IsCancellationRequested) { _pending.Clear(); return []; }
            var entries = _pending.Take(8).ToArray();
            for (var i = 0; i < entries.Length; i++) _pending.Dequeue();
            return entries;
        }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _accepting = false; _pending.Clear(); }
    }
}
