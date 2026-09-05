using DotCraft.Protocol;
using DotCraft.Sessions;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Handles the <c>remoteToolHost/*</c> wire methods, whose surface never carries lease ids, Host
/// instance ids, endpoints, or credential references.
/// </summary>
internal sealed class RemoteToolHostRequestHandler(
    IRemoteToolHostClient? remoteToolHostClient,
    ISessionService sessionService,
    Action<Contract.RemoteToolHostRouteChangedNotification>? broadcastRouteChanged) : IAppServerDomainHandler
{
    private const string Connected = "connected";
    private const string LeaseLost = "leaseLost";
    private const string Disconnected = "disconnected";

    public void RegisterMethods(AppServerMethodTable table)
    {
        table.Map(Contract.AppServerRpc.RemoteToolHostList, HandleListAsync);
        table.Map(Contract.AppServerRpc.RemoteToolHostConnect, HandleConnectAsync);
        table.Map(Contract.AppServerRpc.RemoteToolHostDisconnect, HandleDisconnectAsync);
    }

    private async Task<AppServerTypedResult<Contract.RemoteToolHostListResult>> HandleListAsync(
        AppServerTypedRequest<Contract.RemoteToolHostListParams> request,
        CancellationToken ct)
    {
        var client = RequireClient();
        var threadId = request.Params.ThreadId.IsSet ? request.Params.ThreadId.Value?.Trim() : null;
        var catalog = await client.ListAsync(threadId ?? string.Empty, ct);
        var result = new Contract.RemoteToolHostListResult
        {
            Hosts = [.. catalog.Hosts.Select(ToWire)],
            Route = string.IsNullOrEmpty(threadId)
                ? default(Optional<Contract.RemoteToolRouteInfo?>)
                : Optional<Contract.RemoteToolRouteInfo?>.FromValue(CurrentRoute(client, threadId))
        };
        return AppServerTypedResult<Contract.RemoteToolHostListResult>.FromResult(result);
    }

    private async Task<AppServerTypedResult<Contract.RemoteToolHostConnectResult>> HandleConnectAsync(
        AppServerTypedRequest<Contract.RemoteToolHostConnectParams> request,
        CancellationToken ct)
    {
        var client = RequireClient();
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var hostId = Require(request.Params.HostId, "'hostId' is required.");
        var workspaceId = Require(request.Params.WorkspaceId, "'workspaceId' is required.");
        await RequireIdleThreadAsync(threadId, ct);

        RemoteToolConnectResult connected;
        try
        {
            connected = await client.ConnectAsync(threadId, hostId, workspaceId, ct);
        }
        catch (RemoteToolHostException exception)
        {
            throw AppServerErrors.RemoteToolHost(exception.Code);
        }

        var route = new Contract.RemoteToolRouteInfo
        {
            ThreadId = threadId,
            HostId = connected.Route.HostId,
            WorkspaceId = connected.Route.WorkspaceId,
            Status = Connected,
            Environment = ToWire(connected.Environment)
        };
        Broadcast(threadId, Connected, route);
        return AppServerTypedResult<Contract.RemoteToolHostConnectResult>.FromResult(
            new Contract.RemoteToolHostConnectResult
            {
                Route = route,
                MatchedTools = [.. connected.MatchedTools],
                UnavailableTools = [.. connected.UnavailableTools],
                AlreadyConnected = connected.AlreadyConnected
            });
    }

    private async Task<AppServerTypedResult<Contract.RemoteToolHostDisconnectResult>> HandleDisconnectAsync(
        AppServerTypedRequest<Contract.RemoteToolHostDisconnectParams> request,
        CancellationToken ct)
    {
        var client = RequireClient();
        var threadId = Require(request.Params.ThreadId, "'threadId' is required.");
        var previous = CurrentRoute(client, threadId);

        RemoteToolDisconnectResult result;
        try
        {
            result = await client.DisconnectAsync(threadId, ct);
        }
        catch (RemoteToolHostException exception)
        {
            throw AppServerErrors.RemoteToolHost(exception.Code);
        }

        if (result.Disconnected)
            Broadcast(threadId, Disconnected, route: null);

        return AppServerTypedResult<Contract.RemoteToolHostDisconnectResult>.FromResult(
            new Contract.RemoteToolHostDisconnectResult
            {
                Disconnected = result.Disconnected,
                PreviousRoute = Optional<Contract.RemoteToolRouteInfo?>.FromValue(
                    previous ?? ToWire(threadId, result.PreviousRoute))
            });
    }

    private void Broadcast(string threadId, string reason, Contract.RemoteToolRouteInfo? route) =>
        broadcastRouteChanged?.Invoke(new Contract.RemoteToolHostRouteChangedNotification
        {
            ThreadId = threadId,
            Reason = reason,
            Route = Optional<Contract.RemoteToolRouteInfo?>.FromValue(route)
        });

    private static Contract.RemoteToolRouteInfo? CurrentRoute(IRemoteToolHostClient client, string threadId) =>
        client.TryGetConnectionSnapshot(threadId, out var snapshot)
            ? new Contract.RemoteToolRouteInfo
            {
                ThreadId = threadId,
                HostId = snapshot.HostId,
                WorkspaceId = snapshot.WorkspaceId,
                Status = snapshot.Status == RemoteToolConnectionStatus.LeaseLost ? LeaseLost : Connected,
                Environment = ToWire(snapshot.Environment)
            }
            : null;

    private static Contract.RemoteToolRouteInfo? ToWire(string threadId, RemoteToolRoute? route) =>
        route is null
            ? null
            : new Contract.RemoteToolRouteInfo
            {
                ThreadId = threadId,
                HostId = route.HostId,
                WorkspaceId = route.WorkspaceId,
                Status = Connected
            };

    private static Contract.RemoteToolHostInfo ToWire(RemoteToolHostDescriptor host) => new()
    {
        HostId = host.HostId,
        DisplayName = host.DisplayName,
        Online = host.Online,
        Workspaces = [.. host.Workspaces.Select(ToWire)],
        ErrorCode = host.ErrorCode
    };

    private static Contract.RemoteToolHostWorkspaceInfo ToWire(RemoteToolWorkspaceDescriptor workspace) => new()
    {
        WorkspaceId = workspace.WorkspaceId,
        DisplayName = workspace.DisplayName,
        Available = workspace.Available,
        BusyOwner = workspace.BusyOwner,
        LeaseExpiresAt = workspace.LeaseExpiresAt
    };

    private static Optional<Contract.RemoteToolEnvironmentInfo?> ToWire(RemoteToolEnvironment environment) =>
        Optional<Contract.RemoteToolEnvironmentInfo?>.FromValue(new Contract.RemoteToolEnvironmentInfo
        {
            HostName = environment.HostName,
            OperatingSystem = environment.OperatingSystem,
            UserName = environment.UserName,
            WorkspacePath = environment.WorkspacePath
        });

    private async Task RequireIdleThreadAsync(string threadId, CancellationToken ct)
    {
        SessionThread thread;
        try
        {
            thread = await sessionService.GetThreadAsync(threadId, ct);
        }
        catch (KeyNotFoundException)
        {
            throw AppServerErrors.ThreadNotFound(threadId);
        }

        if (thread.Turns.Any(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput))
        {
            throw AppServerErrors.TurnInProgress(threadId);
        }
    }

    private IRemoteToolHostClient RequireClient() =>
        remoteToolHostClient ?? throw AppServerErrors.MethodNotFound("remoteToolHost/*");

    private static string Require(string value, string detail)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw AppServerErrors.InvalidParams(detail);
        return value.Trim();
    }
}
