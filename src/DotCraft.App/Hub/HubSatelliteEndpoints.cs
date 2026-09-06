using System.Net.WebSockets;
using DotCraft.Logging;
using DotCraft.RemoteTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

/// <summary>
/// The opt-in listener that paired Remote Tool Hosts dial into, a separate application whose own
/// route table makes no Hub Local API route reachable from it.
/// </summary>
internal sealed class HubSatelliteListener : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HubSatelliteListener(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    public static async Task<HubSatelliteListener> StartAsync(
        string host,
        int port,
        SatelliteConnectionManager manager,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new NonOwningLoggerProvider(loggerFactory));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        MapRoutes(app, manager);
        app.Urls.Add($"http://{NormalizeHost(host)}:{port}");

        try
        {
            await app.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await app.DisposeAsync();
            throw new HubProtocolException(
                "portUnavailable",
                $"The satellite listener could not bind port {port}.",
                StatusCodes.Status409Conflict,
                new { port, reason = ex.GetType().Name });
        }

        return new HubSatelliteListener(app, ResolvePort(app, port));
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync(CancellationToken.None);
        await _app.DisposeAsync();
    }

    private static void MapRoutes(WebApplication app, SatelliteConnectionManager manager)
    {
        app.MapGet("/i/{inviteId}", (HttpContext context, string inviteId) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var invite = manager.Registry.FindInvite(inviteId);
            if (invite is null)
                return Results.Text("This invitation is no longer valid.", "text/plain", statusCode: 404);

            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            var url = $"{origin}{SatelliteWire.InvitePathPrefix}{inviteId}";
            var accept = context.Request.Headers.Accept.ToString();
            var details = SatelliteInvitePage.Describe(inviteId, invite, origin);
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return Results.Json(details, HubJson.Options);
            if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                return Results.Content(SatelliteInvitePage.RenderHtml(details, url), "text/html; charset=utf-8");
            return Results.Text(
                SatelliteWire.DescribeInvitePage(
                    $"dotcraft tool-host join {url} --workspace <folder>",
                    invite.ExpiresAt),
                "text/plain");
        });

        app.MapGet(SatelliteInvitePage.InstallerPath, (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var installer = ResolveInstallerPath();
            return installer is null
                ? Results.Text(
                    "This DotCraft build ships no Satellite installer. Download it from "
                    + SatelliteInvitePage.ReleasesUrl + ".",
                    "text/plain",
                    statusCode: 404)
                : Results.File(
                    installer,
                    "application/octet-stream",
                    SatelliteInvitePage.InstallerFileName);
        });

        app.Map(SatelliteWire.ControlPath, async (HttpContext context) =>
        {
            var bearer = SatelliteWire.ReadBearer(context.Request.Headers.Authorization);
            var peerId = context.Request.Query["peer"].FirstOrDefault();
            if (bearer is null || !manager.CanAdmitControl(peerId, bearer))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await manager.RunControlAsync(socket, peerId, bearer, context.RequestAborted);
        });

        app.Map(SatelliteWire.DataPath, async (HttpContext context) =>
        {
            var bearer = SatelliteWire.ReadBearer(context.Request.Headers.Authorization);
            var peerId = context.Request.Query["peer"].FirstOrDefault();
            var sessionId = context.Request.Query["session"].FirstOrDefault();
            if (bearer is null
                || string.IsNullOrEmpty(peerId)
                || string.IsNullOrEmpty(sessionId)
                || !manager.CanAdmitData(peerId, bearer))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var relay = manager.TryAttachData(peerId, sessionId, socket);
            if (relay is null)
            {
                await socket.CloseOutputAsync(
                    WebSocketCloseStatus.EndpointUnavailable,
                    SatelliteWire.SessionFailedClose,
                    context.RequestAborted);
                return;
            }
            await relay;
        });
    }

    /// <summary>The installer is staged beside the executable by the build, not fetched at runtime.</summary>
    private static string? ResolveInstallerPath()
    {
        var directory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var path = Path.Combine(directory, SatelliteInvitePage.InstallerFileName);
        return File.Exists(path) ? path : null;
    }

    private static int ResolvePort(WebApplication app, int configuredPort)
    {
        if (configuredPort != 0)
            return configuredPort;
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var bound = addresses?.Addresses.FirstOrDefault();
        return bound is not null && Uri.TryCreate(bound, UriKind.Absolute, out var uri) ? uri.Port : 0;
    }

    private static string NormalizeHost(string host) =>
        string.IsNullOrWhiteSpace(host) ? "0.0.0.0" : host.Trim();
}
