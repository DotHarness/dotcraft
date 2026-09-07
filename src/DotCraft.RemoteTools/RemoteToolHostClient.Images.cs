using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    public async ValueTask<string> WriteImageAsync(RemoteToolRoute route, string threadId, string callId,
        byte[] bytes, CancellationToken cancellationToken = default)
    {
        SharedLease lease;
        lock (_stateGate)
        {
            if (!_routes.TryGetValue(threadId, out var current) || current != route
                || !_leases.TryGetValue(new RouteKey(route.HostId, route.WorkspaceId), out lease!)
                || lease.Lost || lease.Route != route)
                throw new RemoteToolHostException(RemoteToolErrorCodes.LeaseLost, "The captured image destination is no longer connected.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var result = await SendAsync<RemoteImageWriteRequest, RemoteImageWriteResponse>(
                lease.Session.Client, RemoteImageWriteRequest.Method,
                new(route.LeaseId, route.WorkspaceId, threadId, callId, Convert.ToBase64String(bytes)),
                cancellationToken).ConfigureAwait(false);
            return result.SavedPath;
        }
        catch (RemoteToolHostException) { throw; }
        catch (Exception ex)
        {
            throw new RemoteToolHostException(RemoteToolErrorCodes.RemoteOutcomeUnknown,
                "The remote image write outcome is unknown; it was not retried.", callId, ex);
        }
    }
}

internal sealed record RemoteImageWriteRequest(string LeaseId, string WorkspaceId, string ThreadId, string CallId, string ImageBase64)
{
    public const string Method = "dotcraft/remoteToolHost/images/write";
}

internal sealed record RemoteImageWriteResponse(string SavedPath);
