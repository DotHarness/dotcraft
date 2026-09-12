using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    /// <summary>Prepares the candidate completely before publishing it under the route gate.</summary>
    private async Task<(RemoteToolConnectResult Result, RemoteToolRoute? Published)> ConnectRouteAsync(
        string threadId, string hostId, string workspaceId, CancellationToken cancellationToken)
    {
        if (TryGetRoute(threadId, out var existing) && existing.HostId == hostId && existing.WorkspaceId == workspaceId)
        {
            RequireLease(existing);
            await PreparePluginsAsync(threadId, RequireLease(existing), cancellationToken).ConfigureAwait(false);
            var summary = await BuildMatchSummaryAsync(threadId, existing, cancellationToken).ConfigureAwait(false);
            return (summary with { AlreadyConnected = true }, null);
        }
        var session = await GetSessionAsync(hostId, cancellationToken).ConfigureAwait(false);
        var key = new RouteKey(hostId, workspaceId);
        SharedLease? lease;
        lock (_stateGate) _leases.TryGetValue(key, out lease);
        var created = lease is null;
        try
        {
            if (lease is null)
            {
                WorkspaceAcquireResponse acquired;
                try
                {
                    acquired = await SendAsync<WorkspaceAcquireRequest, WorkspaceAcquireResponse>(
                        session.Client, RemoteToolHostProtocol.WorkspacesAcquire,
                        new(RemoteToolHostProtocol.ProfileVersion, _clientInstanceId, workspaceId), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    throw MapConnectionError(ex, null, session.CloseDescription);
                }
                lease = new SharedLease(new(hostId, workspaceId, acquired.LeaseId, acquired.HostInstanceId),
                    acquired.WorkspacePath, session, _clientInstanceId, OnLeaseLost);
                lock (_stateGate) _leases.Add(key, lease);
                lease.StartHeartbeat();
                var info = await SendAsync<WorkspaceListRequest, WorkspaceListResponse>(session.Client,
                    RemoteToolHostProtocol.WorkspacesList, new(RemoteToolHostProtocol.ProfileVersion, _clientInstanceId),
                    cancellationToken).ConfigureAwait(false);
                ValidateHostIdentity(hostId, info);
                if (info.Capabilities is null || !info.Capabilities.Contains("files-v1"))
                    throw new RemoteToolHostException(RemoteToolErrorCodes.ProtocolMismatch,
                        "The remote Host lacks file transfer support. Upgrade Satellite.");
                CaptureDisplayNames(hostId, info);
                lease.HostName = info.Hostname;
                lease.OperatingSystem = info.Os;
                lease.UserName = info.Username;
                lease.BuildVersion = info.BuildVersion;
                lease.SupportsPlugins = info.Capabilities.Contains(RemotePluginProtocol.Capability);
            }
            RequireLease(lease.Route);
            await PreparePluginsAsync(threadId, lease, cancellationToken).ConfigureAwait(false);
            var summary = await BuildMatchSummaryAsync(threadId, lease.Route, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RemoteToolRoute? previous;
            lock (_stateGate)
            {
                if (lease.Lost) throw new RemoteToolHostException(RemoteToolErrorCodes.LeaseLost, "The candidate lease was lost.");
                _routes.Remove(threadId, out previous);
                _routes[threadId] = lease.Route;
                lease.ReferenceCount++;
            }
            if (previous is not null)
            {
                await ReleasePluginThreadAsync(threadId, previous, CancellationToken.None).ConfigureAwait(false);
                await ReleaseRouteReferenceAsync(previous, CancellationToken.None).ConfigureAwait(false);
            }
            return (summary, lease.Route);
        }
        catch
        {
            if (lease is not null && (!TryGetRoute(threadId, out var current) || current != lease.Route))
                await ReleasePluginThreadAsync(threadId, lease.Route, CancellationToken.None).ConfigureAwait(false);
            if (created && lease is not null && lease.ReferenceCount == 0)
            {
                lock (_stateGate) _leases.Remove(key);
                await lease.DisposeAndReleaseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }
}
