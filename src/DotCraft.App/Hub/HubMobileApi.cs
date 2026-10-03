using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.Hub;

internal static class HubMobileApi
{
    public static void Map(
        IEndpointRouteBuilder app,
        MobileGateway mobile,
        Func<HttpRequest, IResult?> unauthorized,
        Func<Func<Task<IResult>>, Task<IResult>> protectedAsync)
    {
        app.MapGet("/v1/mobile", (HttpRequest request) =>
            unauthorized(request) ?? Results.Json(mobile.Snapshot(), HubJson.Options));

        app.MapPost("/v1/mobile/enable", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(async () => Results.Json(await mobile.EnableAsync(), HubJson.Options)));

        app.MapPost("/v1/mobile/disable", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(async () => Results.Json(await mobile.DisableAsync(), HubJson.Options)));

        app.MapPost("/v1/mobile/pairings", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(() => Task.FromResult(Results.Json(mobile.MintPairing(), HubJson.Options))));

        app.MapPut("/v1/mobile/relay", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(async () => Results.Json(await mobile.SetRelayAsync(await ReadRelayAsync(request)), HubJson.Options)));

        app.MapDelete("/v1/mobile/relay", async (HttpRequest request) =>
            unauthorized(request)
            ?? await protectedAsync(async () => Results.Json(await mobile.RemoveRelayAsync(), HubJson.Options)));

        app.MapDelete("/v1/mobile/devices/{deviceId}", async (HttpRequest request, string deviceId) =>
            unauthorized(request)
            ?? await protectedAsync(async () => await mobile.RevokeAsync(deviceId)
                ? Results.NoContent()
                : throw new HubProtocolException(
                    "deviceNotFound",
                    $"No paired phone with id '{deviceId}'.",
                    StatusCodes.Status404NotFound)));
    }

    private static async Task<MobileRelayRequest> ReadRelayAsync(HttpRequest request)
    {
        try
        {
            return await request.ReadFromJsonAsync<MobileRelayRequest>(HubJson.Options, request.HttpContext.RequestAborted)
                   ?? new MobileRelayRequest();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new MobileRelayRequest();
        }
    }
}
