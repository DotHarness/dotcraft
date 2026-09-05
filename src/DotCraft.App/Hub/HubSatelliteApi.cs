using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using DotCraft.RemoteTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.Hub;

internal static class HubSatelliteApi
{
    public static void Map(
        IEndpointRouteBuilder app,
        HubConfig config,
        SatelliteConnectionManager satellites,
        HubEventBus events,
        Func<HttpRequest, IResult?> unauthorized,
        Func<Func<Task<IResult>>, Task<IResult>> protectedAsync)
    {
        app.MapGet("/v1/satellites", (HttpRequest request) =>
        {
            if (unauthorized(request) is { } denied)
                return denied;
            return Results.Json(
                satellites.Registry.ListPeers()
                    .Select(peer => ToResponse(peer, satellites.IsOnline(peer.PeerId)))
                    .ToArray(),
                HubJson.Options);
        });

        app.MapPost("/v1/satellites/invites", async (
            HttpRequest request,
            CreateSatelliteInviteRequest body,
            CancellationToken ct) =>
        {
            if (unauthorized(request) is { } denied)
                return denied;
            return await protectedAsync(async () =>
            {
                var port = await satellites.EnsureListenerAsync(ct);
                var ttlHours = body.TtlHours is > 0 ? body.TtlHours.Value : config.InviteTtlHours;
                var label = string.IsNullOrWhiteSpace(body.Name) ? Environment.MachineName : body.Name.Trim();
                var (inviteId, expiresAt) = satellites.Registry.CreateInvite(
                    label,
                    string.IsNullOrWhiteSpace(body.Purpose) ? null : body.Purpose.Trim(),
                    string.IsNullOrWhiteSpace(body.Folder) ? null : body.Folder.Trim(),
                    TimeSpan.FromHours(ttlHours));
                var host = string.IsNullOrWhiteSpace(body.Host) ? ResolveAdvertisedHost() : body.Host.Trim();
                var url = $"http://{host}:{port}{SatelliteWire.InvitePathPrefix}{inviteId}";
                return Results.Json(
                    new CreateSatelliteInviteResponse(inviteId, url, expiresAt),
                    HubJson.Options);
            });
        });

        app.MapDelete("/v1/satellites/{peerId}", async (HttpRequest request, string peerId) =>
        {
            if (unauthorized(request) is { } denied)
                return denied;
            return await protectedAsync(async () =>
            {
                if (!satellites.Registry.Revoke(peerId))
                    throw new HubProtocolException(
                        "satelliteNotFound",
                        $"No paired machine with id '{peerId}'.",
                        StatusCodes.Status404NotFound);
                await satellites.RevokeLiveAsync(peerId);
                events.Publish("satellite.revoked", data: new { peerId });
                return Results.Json(new { revoked = true }, HubJson.Options);
            });
        });

        app.Map("/v1/satellites/{peerId}/bridge", async (HttpContext context, string peerId) =>
        {
            if (unauthorized(context.Request) is { } denied)
            {
                await denied.ExecuteAsync(context);
                return;
            }

            var sessionId = context.Request.Query["session"].FirstOrDefault();
            if (string.IsNullOrEmpty(sessionId))
            {
                await WriteErrorAsync(context, "sessionConflict", "A unique session id is required.", 400);
                return;
            }
            if (satellites.Registry.FindPeer(peerId) is null)
            {
                await WriteErrorAsync(context, "satelliteNotFound", $"No paired machine with id '{peerId}'.", 404);
                return;
            }
            if (satellites.IsSessionActive(sessionId))
            {
                await WriteErrorAsync(context, "sessionConflict", "That session id is already open.", 409);
                return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                await WriteErrorAsync(context, "sessionConflict", "The bridge requires a WebSocket upgrade.", 400);
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var failure = await satellites.BridgeAsync(peerId, sessionId, socket, context.RequestAborted);
            if (failure is not null && socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.InternalServerError,
                    failure,
                    CancellationToken.None);
            }
        });
    }

    private static Task WriteErrorAsync(HttpContext context, string code, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(
            new HubErrorResponse(new HubError(code, message, null)),
            HubJson.Options);
    }

    private static HubSatelliteResponse ToResponse(SatellitePeerRecord peer, bool online) => new(
        peer.PeerId,
        peer.DisplayName,
        online,
        peer.MachineName,
        peer.OperatingSystem,
        peer.UserName,
        peer.BuildVersion,
        [.. peer.Workspaces.Select(workspace => new HubSatelliteWorkspaceResponse(
            workspace.WorkspaceId,
            workspace.Path,
            workspace.Busy,
            workspace.BusyOwner,
            workspace.LeaseExpiresAt))],
        peer.PairedAt,
        peer.LastSeenAt);

    // The host name is preferred because the first physical address is often a virtual adapter
    // that the invited machine cannot reach.
    private static string ResolveAdvertisedHost()
    {
        try
        {
            var hostName = Dns.GetHostName();
            if (!string.IsNullOrWhiteSpace(hostName))
                return hostName.Trim();
        }
        catch (SocketException)
        {
        }

        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == OperationalStatus.Up
                               && item.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork
                                        && !IPAddress.IsLoopback(item));
            if (address is not null)
                return address.ToString();
        }
        catch (NetworkInformationException)
        {
        }

        return Environment.MachineName;
    }
}
