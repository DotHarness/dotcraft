using DotCraft.RemoteTools;

namespace DotCraft.Satellite.ViewModels;

internal sealed class IslandApprovalEntry(RemoteToolApprovalRequest request, DateTimeOffset receivedAt)
{
    private readonly TaskCompletionSource<bool> _decision =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RemoteToolApprovalRequest Request { get; } = request;

    /// <summary>When the request reached this machine; the two minutes run from here, not from display.</summary>
    public DateTimeOffset ReceivedAt { get; } = receivedAt;

    public Task<bool> Decision => _decision.Task;

    internal bool Settle(bool allowed) => _decision.TrySetResult(allowed);
}

/// <summary>
/// The serial queue of owner requests: only the head is answerable, and every member runs on the
/// user interface thread.
/// </summary>
internal sealed class IslandApprovalQueue
{
    /// <summary>How long a request stands before it denies itself.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(2);

    private readonly List<IslandApprovalEntry> _pending = [];

    public event EventHandler? Changed;

    public IReadOnlyList<IslandApprovalEntry> Pending => _pending;

    public IslandApprovalEntry? Head => _pending.Count > 0 ? _pending[0] : null;

    public void Add(IslandApprovalEntry entry)
    {
        _pending.Add(entry);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Answer(bool allowed)
    {
        if (Head is not { } head)
            return;
        head.Settle(allowed);
        _pending.Remove(head);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops a request whose caller has gone; an unanswered request counts as a denial.</summary>
    public void Remove(IslandApprovalEntry entry)
    {
        if (!_pending.Remove(entry))
            return;
        entry.Settle(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Expire(DateTimeOffset now) =>
        DenyWhere(entry => now - entry.ReceivedAt >= Window);

    /// <summary>Denies the requests a disconnect, a pause or a revoke has made meaningless; a null peer denies every one.</summary>
    public void Invalidate(string? peerId = null) => DenyWhere(entry =>
        peerId is null || string.Equals(entry.Request.PeerId, peerId, StringComparison.Ordinal));

    private void DenyWhere(Func<IslandApprovalEntry, bool> predicate)
    {
        var denied = _pending.Where(predicate).ToArray();
        if (denied.Length == 0)
            return;
        foreach (var entry in denied)
        {
            entry.Settle(false);
            _pending.Remove(entry);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
