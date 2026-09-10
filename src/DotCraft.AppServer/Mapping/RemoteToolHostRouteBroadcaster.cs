using DotCraft.Protocol;
using DotCraft.Tools;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.AppServer;

/// <summary>
/// Projects <see cref="IRemoteToolHostClient.RouteChanged"/> onto the
/// <c>remoteToolHost/route/changed</c> notification. This is the only emitter of that
/// notification, so wire requests, model tools, and heartbeat-detected lease loss all
/// reach clients through one path.
/// </summary>
public sealed class RemoteToolHostRouteBroadcaster : IDisposable
{
    private const string Connected = "connected";
    private const string Disconnected = "disconnected";
    private const string LeaseLost = "leaseLost";

    private readonly IRemoteToolHostClient _client;
    private readonly Action<Contract.RemoteToolHostRouteChangedNotification> _broadcast;

    /// <summary>Subscribes to <paramref name="client"/> until disposed.</summary>
    public RemoteToolHostRouteBroadcaster(
        IRemoteToolHostClient client,
        Action<Contract.RemoteToolHostRouteChangedNotification> broadcast)
    {
        _client = client;
        _broadcast = broadcast;
        _client.RouteChanged += OnRouteChanged;
    }

    /// <inheritdoc />
    public void Dispose() => _client.RouteChanged -= OnRouteChanged;

    private void OnRouteChanged(RemoteToolRouteChange change) =>
        _broadcast(new Contract.RemoteToolHostRouteChangedNotification
        {
            ThreadId = change.ThreadId,
            Reason = ToWire(change.Reason),
            Initiator = Optional<string>.FromValue(ToWire(change.Initiator)),
            Route = Optional<Contract.RemoteToolRouteInfo?>.FromValue(ToWire(_client, change))
        });

    private static Contract.RemoteToolRouteInfo? ToWire(IRemoteToolHostClient client, RemoteToolRouteChange change)
    {
        // A disconnect returns the thread to local execution, so the wire carries no route.
        if (change.Route is null || change.Reason == RemoteToolRouteChangeReason.Disconnected)
            return null;
        if (client.TryGetConnectionSnapshot(change.ThreadId, out var snapshot))
        {
            return new Contract.RemoteToolRouteInfo
            {
                ThreadId = change.ThreadId,
                HostId = snapshot.HostId,
                WorkspaceId = snapshot.WorkspaceId,
                Status = snapshot.Status == RemoteToolConnectionStatus.LeaseLost ? LeaseLost : Connected,
                Environment = Optional<Contract.RemoteToolEnvironmentInfo?>.FromValue(
                    new Contract.RemoteToolEnvironmentInfo
                    {
                        HostName = snapshot.Environment.HostName,
                        OperatingSystem = snapshot.Environment.OperatingSystem,
                        UserName = snapshot.Environment.UserName,
                        WorkspacePath = snapshot.Environment.WorkspacePath
                    })
            };
        }

        return new Contract.RemoteToolRouteInfo
        {
            ThreadId = change.ThreadId,
            HostId = change.Route.HostId,
            WorkspaceId = change.Route.WorkspaceId,
            Status = change.Reason == RemoteToolRouteChangeReason.LeaseLost ? LeaseLost : Connected
        };
    }

    private static string ToWire(RemoteToolRouteChangeReason reason) => reason switch
    {
        RemoteToolRouteChangeReason.Connected => Connected,
        RemoteToolRouteChangeReason.Disconnected => Disconnected,
        _ => LeaseLost
    };

    private static string ToWire(RemoteToolRouteInitiator initiator) => initiator switch
    {
        RemoteToolRouteInitiator.Agent => "agent",
        RemoteToolRouteInitiator.System => "system",
        _ => "client"
    };
}
