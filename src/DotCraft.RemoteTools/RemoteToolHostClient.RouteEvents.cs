using DotCraft.Tools;

namespace DotCraft.RemoteTools;

internal sealed partial class RemoteToolHostClient
{
    private readonly Dictionary<string, string> _hostDisplayNames = new(StringComparer.Ordinal);
    private readonly Dictionary<RouteKey, string> _workspaceDisplayNames = new();

    public event Action<RemoteToolRouteChange>? RouteChanged;

    private void CaptureDisplayNames(IReadOnlyList<RemoteToolHostDescriptor> descriptors)
    {
        lock (_stateGate)
        {
            foreach (var host in descriptors)
            {
                _hostDisplayNames[host.HostId] = host.DisplayName;
                foreach (var workspace in host.Workspaces)
                    _workspaceDisplayNames[new RouteKey(host.HostId, workspace.WorkspaceId)] = workspace.DisplayName;
            }
        }
    }

    private void CaptureDisplayNames(string hostId, WorkspaceListResponse response)
    {
        lock (_stateGate)
        {
            _hostDisplayNames[hostId] = response.DisplayName;
            foreach (var workspace in response.Workspaces)
                _workspaceDisplayNames[new RouteKey(hostId, workspace.WorkspaceId)] = workspace.Path;
        }
    }

    /// <summary>Publishes one route transition. Always called outside <c>_stateGate</c> and <c>_routeGate</c>.</summary>
    private void RaiseRouteChanged(
        string threadId,
        RemoteToolRouteChangeReason reason,
        RemoteToolRouteInitiator initiator,
        RemoteToolRoute? route)
    {
        var handler = RouteChanged;
        if (handler is null)
            return;

        string? hostDisplayName = null;
        string? workspaceDisplayName = null;
        if (route is not null)
        {
            var key = new RouteKey(route.HostId, route.WorkspaceId);
            lock (_stateGate)
            {
                if (!_hostDisplayNames.TryGetValue(route.HostId, out hostDisplayName)
                    && _leases.TryGetValue(key, out var lease))
                {
                    hostDisplayName = lease.HostName;
                }
                _workspaceDisplayNames.TryGetValue(key, out workspaceDisplayName);
            }
        }

        var change = new RemoteToolRouteChange(
            threadId,
            reason,
            initiator,
            route,
            hostDisplayName,
            workspaceDisplayName);
        foreach (var observer in handler.GetInvocationList().Cast<Action<RemoteToolRouteChange>>())
        {
            try
            {
                observer(change);
            }
            catch
            {
                // An observer must not fail the operation that produced the change, nor the observers behind it.
            }
        }
    }
}
