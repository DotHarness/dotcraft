namespace DotCraft.RemoteTools;

internal sealed class WorkspaceLeaseManager(
    TimeProvider? timeProvider = null,
    Action<WorkspaceLeaseReleased>? onReleased = null)
{
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(60);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Action<WorkspaceLeaseReleased>? _onReleased = onReleased;
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _byWorkspace = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Lease> _byId = new(StringComparer.Ordinal);

    public WorkspaceAcquireResponse Acquire(
        string ownerId,
        string workspaceId,
        string workspacePath,
        string hostInstanceId,
        long catalogRevision)
    {
        lock (_gate)
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
            return lease.ToResponse(hostInstanceId, catalogRevision);
        }
    }

    public DateTimeOffset Heartbeat(string ownerId, string leaseId, string workspaceId)
    {
        lock (_gate)
        {
            var lease = ValidateCore(ownerId, leaseId, workspaceId);
            lease.ExpiresAt = _timeProvider.GetUtcNow() + LeaseTtl;
            return lease.ExpiresAt;
        }
    }

    public bool Release(string ownerId, string leaseId, string workspaceId)
    {
        lock (_gate)
        {
            var lease = ValidateCore(ownerId, leaseId, workspaceId);
            if (--lease.ReferenceCount > 0)
                return false;
            RemoveCore(lease);
            return true;
        }
    }

    public string Validate(string leaseId, string workspaceId)
    {
        lock (_gate)
        {
            ReapExpiredCore();
            if (!_byId.TryGetValue(leaseId, out var lease)
                || !string.Equals(lease.WorkspaceId, workspaceId, StringComparison.Ordinal))
            {
                throw new RemoteToolHostException(
                    Tools.RemoteToolErrorCodes.LeaseLost,
                    "The Remote Tool Host workspace lease is missing or expired.");
            }
            return lease.WorkspacePath;
        }
    }

    public bool IsBusy(string workspaceId)
    {
        lock (_gate)
        {
            ReapExpiredCore();
            return _byWorkspace.ContainsKey(workspaceId);
        }
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            foreach (var lease in _byId.Values.ToArray())
                RemoveCore(lease);
        }
    }

    private Lease ValidateCore(string ownerId, string leaseId, string workspaceId)
    {
        ReapExpiredCore();
        if (!_byId.TryGetValue(leaseId, out var lease)
            || !string.Equals(lease.OwnerId, ownerId, StringComparison.Ordinal)
            || !string.Equals(lease.WorkspaceId, workspaceId, StringComparison.Ordinal))
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
