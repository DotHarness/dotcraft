using System.Net;
using System.Net.Http.Headers;

namespace DotCraft.Oratorio.Tests;

public sealed class ManagedServiceAuthenticationTests
{
    [Fact]
    public async Task ManagedMode_LeavesHealthAnonymousAndProtectsDomainApi()
    {
        await using var app = new TestOratorioApp(settings: new Dictionary<string, string?>
        {
            ["DOTCRAFT_MANAGED_SERVICE_TOKEN"] = "managed-secret"
        });
        using var client = app.CreateClient();

        var health = await client.GetAsync("/health");
        var unauthorized = await client.GetAsync("/api/v1/status");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "managed-secret");
        var authorized = await client.GetAsync("/api/v1/status");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }
}
