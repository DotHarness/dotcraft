using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DotCraft.Logging;
using DotCraft.RemoteTools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hub;

internal sealed class HubMobileListener : IAsyncDisposable
{
    private readonly WebApplication _app;

    private HubMobileListener(WebApplication app, IPEndPoint localEndPoint)
    {
        _app = app;
        LocalEndPoint = localEndPoint;
    }

    public IPEndPoint LocalEndPoint { get; }

    public static async Task<HubMobileListener> StartAsync(
        string host,
        int port,
        X509Certificate2 certificate,
        MobileGateway gateway,
        ILoggerFactory loggerFactory)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new NonOwningLoggerProvider(loggerFactory));
        var address = ParseHost(host);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(address, port, listen =>
        {
            listen.Use(next => connection =>
            {
                if (connection.RemoteEndPoint is IPEndPoint remote && MobileNetwork.IsAllowedSource(remote.Address))
                    return next(connection);
                connection.Abort();
                return Task.CompletedTask;
            });
            listen.UseHttps(certificate);
        }));
        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
        MobileGatewayRoutes.Map(app, gateway);

        try
        {
            await app.StartAsync();
        }
        catch (Exception ex)
        {
            await app.DisposeAsync();
            throw new HubProtocolException(
                "portUnavailable",
                $"Phone access could not use port {port}.",
                StatusCodes.Status409Conflict,
                new { port, reason = ex.GetType().Name });
        }

        return new HubMobileListener(app, new IPEndPoint(LocalAddress(address), port));
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await _app.StopAsync(timeout.Token);
        await _app.DisposeAsync();
    }

    private static IPAddress ParseHost(string host) =>
        IPAddress.TryParse(host?.Trim(), out var address) ? address : IPAddress.Any;

    private static IPAddress LocalAddress(IPAddress bound) =>
        bound.Equals(IPAddress.Any) ? IPAddress.Loopback
        : bound.Equals(IPAddress.IPv6Any) ? IPAddress.IPv6Loopback
        : bound;
}

internal static class MobileGatewayRoutes
{
    public static void Map(WebApplication app, MobileGateway gateway)
    {
        app.MapPost("/m/pair", async (HttpContext context) =>
        {
            MobilePairRequest? body;
            try
            {
                body = await context.Request.ReadFromJsonAsync<MobilePairRequest>(HubJson.Options, context.RequestAborted);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                body = null;
            }
            return Protected(() => body is null
                ? throw new HubProtocolException("invalidRequest", "The pairing request is not valid JSON.", StatusCodes.Status400BadRequest)
                : Results.Json(gateway.Pair(body), HubJson.Options));
        });

        app.MapGet("/m/hello", (HttpRequest request) =>
            Authenticate(request, gateway) is { } device
                ? Protected(() => Results.Json(gateway.Hello(device), HubJson.Options))
                : Unauthorized());

        app.MapGet("/m/projects", (HttpRequest request) =>
            Authenticate(request, gateway) is not null
                ? Protected(() => Results.Json(new MobileProjectList(gateway.Projects.List()), HubJson.Options))
                : Unauthorized());

        app.MapPost("/m/projects/{projectId}/ensure", async (HttpRequest request, string projectId, CancellationToken ct) =>
        {
            if (Authenticate(request, gateway) is null)
                return Unauthorized();
            try
            {
                return Results.Json(await gateway.Projects.EnsureAsync(projectId, ct), HubJson.Options);
            }
            catch (Exception ex)
            {
                return ToErrorResult(ex);
            }
        });

        app.Map("/m/projects/{projectId}/appserver", async (HttpContext context, string projectId) =>
        {
            if (Authenticate(context.Request, gateway) is not { } device)
            {
                await Unauthorized().ExecuteAsync(context);
                return;
            }
            await gateway.RelayAsync(context, device, projectId);
        });

        app.Map("/m/events", async (HttpContext context) =>
        {
            if (Authenticate(context.Request, gateway) is not { } device)
            {
                await Unauthorized().ExecuteAsync(context);
                return;
            }
            await gateway.RunEventsAsync(context, device);
        });

        app.MapDelete("/m/device", async (HttpRequest request) =>
        {
            if (Authenticate(request, gateway) is not { } device)
                return Unauthorized();
            await gateway.RevokeAsync(device.DeviceId);
            return Results.NoContent();
        });
    }

    public static Task WriteErrorAsync(HttpContext context, string code, string message, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(new HubErrorResponse(new HubError(code, message, null)), HubJson.Options);
    }

    private static MobileDeviceRecord? Authenticate(HttpRequest request, MobileGateway gateway) =>
        SatelliteWire.ReadBearer(request.Headers.Authorization) is { } credential
            ? gateway.Registry.FindByCredential(credential)
            : null;

    private static IResult Unauthorized() => Error(
        "unauthorized",
        "Missing, unknown, or revoked device credential.",
        StatusCodes.Status401Unauthorized);

    private static IResult Protected(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return ToErrorResult(ex);
        }
    }

    private static IResult ToErrorResult(Exception exception) => exception is HubProtocolException protocol
        ? Error(protocol.Code, protocol.Message, protocol.StatusCode)
        : Error("hubInternalError", "Hub encountered an unexpected internal error.", StatusCodes.Status500InternalServerError);

    private static IResult Error(string code, string message, int statusCode) =>
        Results.Json(new HubErrorResponse(new HubError(code, message, null)), HubJson.Options, statusCode: statusCode);
}
