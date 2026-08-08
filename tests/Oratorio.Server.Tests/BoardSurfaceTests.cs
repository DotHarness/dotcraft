using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Oratorio.Server.DotCraft;

namespace Oratorio.Server.Tests;

public sealed class BoardSurfaceTests
{
    [Fact]
    public async Task BoardSurface_RequiresBearerAndReusesExistingApiEndpoints()
    {
        await using var app = new TestOratorioApp();
        var client = app.CreateClient();
        var surface = app.Services.GetRequiredService<OratorioBoardSurfaceRuntime>();
        const string surfaceStatusPath = "/dotcraft/surfaces/board/api/v1/status";

        using var missingBearer = await client.GetAsync(surfaceStatusPath);
        Assert.Equal(HttpStatusCode.Unauthorized, missingBearer.StatusCode);

        using var wrongBearerRequest = new HttpRequestMessage(HttpMethod.Get, surfaceStatusPath);
        wrongBearerRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-bearer");
        using var wrongBearer = await client.SendAsync(wrongBearerRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongBearer.StatusCode);

        using var authorizedRequest = new HttpRequestMessage(HttpMethod.Get, surfaceStatusPath);
        authorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", surface.Bearer);
        using var authorized = await client.SendAsync(authorizedRequest);
        authorized.EnsureSuccessStatusCode();

        using var direct = await client.GetAsync("/api/v1/status");
        direct.EnsureSuccessStatusCode();
        Assert.Equal(
            await direct.Content.ReadAsStringAsync(),
            await authorized.Content.ReadAsStringAsync());
    }
}
