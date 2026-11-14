using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;

namespace DotCraft.Hub;

internal static class HubCodexRemoteApi
{
    public static void Map(
        IEndpointRouteBuilder app,
        CodexGateway gateway,
        Func<HttpRequest, IResult?> unauthorized,
        Func<Func<Task<IResult>>, Task<IResult>> protectedAsync)
    {
        app.MapGet("/v1/codex-remote", (HttpRequest request) =>
            unauthorized(request) ?? Results.Json(gateway.Snapshot(), HubJson.Options));
        app.MapPost("/v1/codex-remote/enable", async (HttpRequest request, CancellationToken ct) =>
            unauthorized(request) ?? await protectedAsync(async () =>
                Results.Json(await gateway.EnableAsync(ct), HubJson.Options)));
        app.MapPost("/v1/codex-remote/disable", async (HttpRequest request, CancellationToken ct) =>
            unauthorized(request) ?? await protectedAsync(async () =>
                Results.Json(await gateway.DisableAsync(ct), HubJson.Options)));
        app.MapPost("/v1/codex-remote/pairings", async (HttpRequest request, CancellationToken ct) =>
            unauthorized(request) ?? await protectedAsync(async () =>
                Results.Json(await gateway.CreatePairingAsync(ct), HubJson.Options)));
    }
}
