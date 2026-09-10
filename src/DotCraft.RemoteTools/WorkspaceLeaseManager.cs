using DotCraft.Tools;

namespace DotCraft.RemoteTools;

/// <summary>
/// One lease per workspace, shared between callers of the same owner. Every change to the set of
/// live leases, including an expiry the clock notices on its own, is reported through
/// <c>onChanged</c>, so the host's status follows the leases rather than the traffic.
/// </summary>
internal sealed class WorkspaceLeaseManager(
    TimeProvider? timeProvider = null,
    Action<WorkspaceLeaseReleased>? onReleased = null,
    Action? onChanged = null) : IDisposable
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Action<WorkspaceLeaseReleased>? _onReleased = onReleased;
    private readonly Action? _onChanged = onChanged;
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _byWorkspace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lease> _byId = new(StringComparer.Ordinal);
    private ITimer? _expiry;
    private bool _changed;
    private bool _disposed;

    public WorkspaceAcquireResponse Acquire(
        string ownerId,
        string workspaceId,
        string workspacePath,
        string hostInstanceId,
        long catalogRevision) => Locked(() =>
    {
        ReapExpiredCore();
        if (_byWorkspace.TryGetValue(workspaceId, out var existing))
        {
            if (!string.Equals(existing.OwnerId, ownerId, StringComparison.Ordinal))
                throw new RemoteToolHostException(
                    Tools.RemoteToolErrorCodes.WorkspaceBusy,
                    $"Workspace '{workspaceId}' is leased by another Agent Host.");

            existing.ReferenceCount++;
            existing.ExpiresAt = _timeProvider.GetUtcNow() + LeaseTtl;
            Arm();
            return existing.ToResponse(hostInstanceId, catalogRevision);
        }

        var lease = new Lease
        {
            LeaseId = "lease_" + Guid.NewGuid().ToString("N"),
            OwnerId = ownerId,
            WorkspaceId = workspaceId,
            WorkspacePath = workspacePath,
            ExpiresAt = _timeProvider.GetUtcNow() + LeaseTtl,
            ReferenceCount = 1
        };
        _byWorkspace.Add(workspaceId, lease);
        _byId.Add(lease.LeaseId, lease);
        _changed = true;
        Arm();
        return lease.ToResponse(hostInstanceId, catalogRevision);
    });

    public DateTimeOffset Heartbeat(string ownerId, string leaseId, string workspaceId) => Locked(() =>
    {
        var lease = ValidateCore(ownerId, leaseId, workspaceId);
        lease.ExpiresAt = _timeProvider.GetUtcNow() + LeaseTtl;
        Arm();
        return lease.ExpiresAt;
    });

    public bool Release(string ownerId, string leaseId, string workspaceId) => Locked(() =>
    {
        var lease = ValidateCore(ownerId, leaseId, workspaceId);
        if (--lease.ReferenceCount > 0)
            return false;
        RemoveCore(lease);
        return true;
    });

    public string Validate(string leaseId, string workspaceId) => Locked(() => Find(leaseId, workspaceId).WorkspacePath);

    public void CommitArtifact(string leaseId, string workspaceId, Action commit) => Locked(() =>
    {
        Find(leaseId, workspaceId);
        commit();
    });

    public bool HasActiveLease => Locked(() =>
    {
        ReapExpiredCore();
        return _byWorkspace.Count > 0;
    });

    public WorkspaceLeaseStatus? GetStatus(string workspaceId) => Locked(() =>
    {
        ReapExpiredCore();
        return _byWorkspace.TryGetValue(workspaceId, out var lease)
            ? new WorkspaceLeaseStatus(lease.OwnerId, lease.ExpiresAt)
            : null;
    });

    public void ReleaseWorkspace(string workspaceId) => Locked(() =>
    {
        if (_byWorkspace.TryGetValue(workspaceId, out var lease))
            RemoveCore(lease);
    });

    public void ReleaseAll() => Locked(() =>
    {
        foreach (var lease in _byId.Values.ToArray())
            RemoveCore(lease);
    });

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _expiry?.Dispose();
            _expiry = null;
        }
    }

    /// <summary>Runs <paramref name="body"/> under the lock and reports a changed live set once the lock is released.</summary>
    private T Locked<T>(Func<T> body)
    {
        try
        {
            lock (_gate)
                return body();
        }
        finally
        {
            Announce();
        }
    }

    private void Locked(Action body)
    {
        try
        {
            lock (_gate)
                body();
        }
        finally
        {
            Announce();
        }
    }

    private void Announce()
    {
        bool changed;
        lock (_gate)
        {
            changed = _changed;
            _changed = false;
        }
        if (changed)
            _onChanged?.Invoke();
    }

    /// <summary>Wakes at the earliest expiry, so a lease whose owner went quiet is dropped without waiting for traffic.</summary>
    private void Arm()
    {
        if (_disposed)
            return;
        _expiry ??= _timeProvider.CreateTimer(
            _ => Locked(() =>
            {
                ReapExpiredCore();
                Arm();
            }),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        var due = _byId.Count == 0
            ? Timeout.InfiniteTimeSpan
            : _byId.Values.Min(lease => lease.ExpiresAt) - _timeProvider.GetUtcNow();
        _expiry.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private Lease Find(string leaseId, string workspaceId)
    {
        ReapExpiredCore();
        if (!_byId.TryGetValue(leaseId, out var lease)
            || !string.Equals(lease.WorkspaceId, workspaceId, StringComparison.Ordinal))
        {
            throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.LeaseLost,
                "The Remote Tool Host workspace lease is missing or expired.");
        }
        return lease;
    }

    private Lease ValidateCore(string ownerId, string leaseId, string workspaceId)
    {
        var lease = Find(leaseId, workspaceId);
        if (!string.Equals(lease.OwnerId, ownerId, StringComparison.Ordinal))
        {
            throw new RemoteToolHostException(
                Tools.RemoteToolErrorCodes.LeaseLost,
                "The Remote Tool Host workspace lease is missing or expired.");
        }
        return lease;
    }

    private void ReapExpiredCore()
    {
        var now = _timeProvider.GetUtcNow();
        foreach (var lease in _byId.Values.Where(item => item.ExpiresAt <= now).ToArray())
            RemoveCore(lease);
    }

    private void RemoveCore(Lease lease)
    {
        _byId.Remove(lease.LeaseId);
        _byWorkspace.Remove(lease.WorkspaceId);
        _changed = true;
        _onReleased?.Invoke(new WorkspaceLeaseReleased(
            lease.LeaseId,
            lease.WorkspaceId,
            lease.WorkspacePath));
    }

    private sealed class Lease
    {
        public required string LeaseId { get; init; }
        public required string OwnerId { get; init; }
        public required string WorkspaceId { get; init; }
        public required string WorkspacePath { get; init; }
        public required DateTimeOffset ExpiresAt { get; set; }
        public int ReferenceCount { get; set; }

        public WorkspaceAcquireResponse ToResponse(string hostInstanceId, long catalogRevision) =>
            new(LeaseId, WorkspaceId, WorkspacePath, ExpiresAt, hostInstanceId, catalogRevision);
    }
}

internal sealed record WorkspaceLeaseReleased(
    string LeaseId,
    string WorkspaceId,
    string WorkspacePath);

internal sealed record WorkspaceLeaseStatus(string OwnerId, DateTimeOffset ExpiresAt);
