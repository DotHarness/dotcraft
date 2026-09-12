using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostMcpHandlers
{
    private readonly RemoteOperationScope _operations = new();
    private readonly Dictionary<string, RemoteOperationScope> _threads = new(StringComparer.Ordinal);
    private WorkspaceAcquireResponse? _admission;
    private string? _ownerId;
    private Task? _closing;
    private Task? _disposal;
    internal string SessionId { get; } = "session_" + Guid.NewGuid().ToString("N");
    internal string PeerId { get; }
    internal string? WorkspaceId { get { lock (_gate) return _admission?.WorkspaceId; } }
    private string ArtifactRoot => Path.Combine(_storage.ArtifactsRootPath, SessionId);

    internal async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> execute, CancellationToken ct)
    {
        RemoteOperationScope.Call operation;
        lock (_gate)
        {
            if (_closing is not null) throw Lost();
            operation = _operations.Enter(ct);
        }
        using (operation) return await execute(operation.Token).ConfigureAwait(false);
    }

    private WorkspaceAcquireResponse Acquire(WorkspaceAcquireRequest input, string path, long revision)
    {
        lock (_gate)
        {
            if (_closing is not null) throw Lost();
            if (_admission is not null)
            {
                if (_ownerId != input.ClientInstanceId || _admission.WorkspaceId != input.WorkspaceId) throw Lost();
                ValidateLease(_admission.LeaseId, _admission.WorkspaceId);
                return _admission;
            }
            _admission = _leases.Acquire(input.ClientInstanceId, input.WorkspaceId, path, _host.InstanceId, revision);
            _ownerId = input.ClientInstanceId;
            return _admission;
        }
    }

    private string ValidateLease(string leaseId, string workspaceId)
    {
        lock (_gate)
        {
            if (_closing is not null || _admission?.LeaseId != leaseId || _admission.WorkspaceId != workspaceId) throw Lost();
            return _leases.Validate(leaseId, workspaceId);
        }
    }

    private WorkspaceLeaseManager.LeaseCall EnterCall(string leaseId, string workspaceId)
    {
        ValidateLease(leaseId, workspaceId);
        return _leases.EnterCall(leaseId, workspaceId);
    }

    private void CommitArtifact(string leaseId, string workspaceId, Action commit)
    {
        lock (_gate)
        {
            ValidateLease(leaseId, workspaceId);
            _leases.CommitArtifact(leaseId, workspaceId, commit);
        }
    }

    private string ScopedThread(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch,
                "Remote execution requires a Thread identity.");
        var scoped = SessionId + "_" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(threadId)));
        lock (_gate)
            if (!_threads.ContainsKey(scoped)) _threads.Add(scoped, new());
        return scoped;
    }

    internal Task CloseResourcesAsync()
    {
        lock (_gate) return _closing ??= CloseResourcesCoreAsync();
    }

    private async Task CloseResourcesCoreAsync()
    {
        await Task.Yield();
        await _operations.DisposeAsync().ConfigureAwait(false);
        foreach (var preparation in _pluginPreparations.Values)
            await CleanupPreparationAsync(preparation).ConfigureAwait(false);
        _pluginPreparations.Clear();
        foreach (var transfer in _transfers.Values)
            await transfer.Session.DisposeAsync().ConfigureAwait(false);
        _transfers.Clear();
        foreach (var thread in _threads.Values) await thread.DisposeAsync().ConfigureAwait(false);
        if (_runtime is { } runtime)
        {
            foreach (var thread in _threads.Keys)
            {
                using var scope = ExecutionSessionTerminals.Enter(runtime.Terminals, thread);
                await runtime.ReleasePluginThreadAsync(thread, CancellationToken.None).ConfigureAwait(false);
            }
            await runtime.DisposeAsync().ConfigureAwait(false);
        }
        _runtime = null;
        if (_admission is { } lease)
        {
            _leaseTerminals.ReleaseLease(lease.LeaseId);
            var data = Path.Combine(_storage.RootPath, "workspaces", lease.WorkspaceId, "sessions", SessionId);
            TransferFileTree.RejectLinks(data);
            if (Directory.Exists(data)) Directory.Delete(data, recursive: true);
        }
        RemoteToolArtifactStore.CleanupStaleArtifacts(ArtifactRoot);
        _pluginApprovals.Clear();
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate) return new(_disposal ??= DisposeSessionAsync());
    }

    private async Task DisposeSessionAsync()
    {
        await CloseResourcesAsync().ConfigureAwait(false);
        if (_admission is { } lease)
        {
            try { _leases.Release(_ownerId!, lease.LeaseId, lease.WorkspaceId); }
            catch (RemoteToolHostException exception) when (exception.Code == RemoteToolErrorCodes.LeaseLost) { }
            await _leases.WaitForDrainAsync(lease.WorkspaceId).ConfigureAwait(false);
        }
        _host.Remove(this);
    }

    private static RemoteToolHostException Lost() => new(RemoteToolErrorCodes.LeaseLost,
        "This connection has no active execution-session lease.");
}
