using System.Net;
using System.Text.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubProjectsTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubProjects_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OpenListAndRemove_KeepTheMostRecentlyOpenedFirst()
    {
        var alpha = CreateFolder("alpha");
        var beta = CreateFolder("beta");
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());

        var opened = await hub.JsonAsync(HttpMethod.Post, "/v1/projects/open", new { path = alpha + Path.DirectorySeparatorChar });
        Assert.Equal(alpha, opened.GetProperty("path").GetString());
        Assert.Equal("alpha", opened.GetProperty("displayName").GetString());
        Assert.False(opened.GetProperty("running").GetBoolean());
        var firstOpenedAt = opened.GetProperty("lastOpenedAt").GetDateTimeOffset();
        await hub.JsonAsync(HttpMethod.Post, "/v1/projects/open", new { path = beta });
        Assert.Equal(["beta", "alpha"], await ListNamesAsync(hub));

        var reopened = await hub.JsonAsync(HttpMethod.Post, "/v1/projects/open", new { path = alpha });
        Assert.True(reopened.GetProperty("lastOpenedAt").GetDateTimeOffset() > firstOpenedAt);
        Assert.Equal(["alpha", "beta"], await ListNamesAsync(hub));

        await hub.JsonAsync(HttpMethod.Post, "/v1/projects/remove", new { path = beta });
        Assert.Equal(["alpha"], await ListNamesAsync(hub));
        Assert.True(Directory.Exists(beta));

        await AssertErrorAsync(
            await hub.SendAsync(HttpMethod.Post, "/v1/projects/remove", body: new { path = beta }),
            HttpStatusCode.NotFound,
            "projectNotFound");
        await AssertErrorAsync(
            await hub.SendAsync(HttpMethod.Post, "/v1/projects/open", body: new { path = Path.Combine(_userProfile, "missing") }),
            HttpStatusCode.NotFound,
            "workspaceNotFound");
    }

    [Fact]
    public async Task Projects_SurviveAHubRestart()
    {
        var alpha = CreateFolder("alpha");
        await using (var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort()))
            await hub.JsonAsync(HttpMethod.Post, "/v1/projects/open", new { path = alpha });

        await using var restarted = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());
        Assert.Equal(["alpha"], await ListNamesAsync(restarted));
    }

    private string CreateFolder(string name)
    {
        var path = Path.Combine(_userProfile, "workspaces", name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string[]> ListNamesAsync(MobileHubFixture hub) =>
        [.. (await hub.JsonAsync(HttpMethod.Get, "/v1/projects")).GetProperty("projects").EnumerateArray()
            .Select(project => project.GetProperty("displayName").GetString()!)];

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(status == response.StatusCode, $"Expected {status}, got {response.StatusCode}: {body}");
            Assert.Equal(code, JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString());
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }
}
