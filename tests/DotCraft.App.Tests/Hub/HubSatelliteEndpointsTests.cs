using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using DotCraft.Hub;
using DotCraft.RemoteTools;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubSatelliteEndpointsTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubSatellite_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SatelliteEndpoints_RequireHubToken()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        Assert.Equal(HttpStatusCode.Unauthorized, (await hub.Http.GetAsync($"{hub.ApiBaseUrl}/v1/satellites")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await hub.Http.PostAsJsonAsync($"{hub.ApiBaseUrl}/v1/satellites/invites", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await hub.Http.DeleteAsync($"{hub.ApiBaseUrl}/v1/satellites/sat_missing")).StatusCode);
    }

    [Fact]
    public async Task Invite_StartsSatelliteListener_AndReturnsReachableUrl()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        var invite = await hub.CreateInviteAsync("Ann");

        var page = await hub.Http.GetAsync(invite.Url);
        page.EnsureSuccessStatusCode();
        var body = await page.Content.ReadAsStringAsync();
        Assert.Contains("dotcraft tool-host join", body, StringComparison.Ordinal);
        Assert.Contains(invite.InviteId, body, StringComparison.Ordinal);
        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
        Assert.True(invite.ExpiresAt > DateTimeOffset.UtcNow.AddHours(23));
    }

    [Fact]
    public async Task InvitePage_ServesJsonDetailsAndHtmlWithoutConsumingTheInvitation()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var invite = await hub.CreateInviteAsync("Ann", "Fix the build");

        using var jsonRequest = new HttpRequestMessage(HttpMethod.Get, invite.Url);
        jsonRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var jsonResponse = await hub.Http.SendAsync(jsonRequest);
        jsonResponse.EnsureSuccessStatusCode();
        using var details = JsonDocument.Parse(await jsonResponse.Content.ReadAsStringAsync());
        Assert.Equal(invite.InviteId, details.RootElement.GetProperty("inviteId").GetString());
        Assert.Equal("Ann", details.RootElement.GetProperty("inviterDisplayName").GetString());
        Assert.Equal("Fix the build", details.RootElement.GetProperty("purpose").GetString());
        Assert.Equal(
            new Uri(invite.Url).GetLeftPart(UriPartial.Authority),
            details.RootElement.GetProperty("hubEndpoint").GetString());

        using var htmlRequest = new HttpRequestMessage(HttpMethod.Get, invite.Url);
        htmlRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using var htmlResponse = await hub.Http.SendAsync(htmlRequest);
        var html = await htmlResponse.Content.ReadAsStringAsync();
        Assert.Equal("text/html", htmlResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Ann", html, StringComparison.Ordinal);
        Assert.Contains("Fix the build", html, StringComparison.Ordinal);
        Assert.Contains("/satellite/installer", html, StringComparison.Ordinal);
        Assert.Contains("dotcraft://satellite/join?invite=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://cdn", html, StringComparison.OrdinalIgnoreCase);

        var plain = await hub.Http.GetStringAsync(invite.Url);
        Assert.Contains("dotcraft tool-host join", plain, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallerRoute_PointsAtReleases_WhenNoInstallerShips()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var invite = await hub.CreateInviteAsync("Ann");
        var listener = new Uri(invite.Url).GetLeftPart(UriPartial.Authority);

        using var response = await hub.Http.GetAsync($"{listener}/satellite/installer");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(
            "github.com/DotHarness/dotcraft/releases",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_PublishesSatelliteRevokedEvent()
    {
        var paths = HubPaths.Resolve(_userProfile);
        Directory.CreateDirectory(paths.HubStatePath);
        var registry = new SatelliteRegistry(paths.SatellitesPath);
        var (peer, _) = registry.Pair("Ann", new SatelliteFrame { Kind = "hello", MachineName = "ANN-PC" });
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        using var eventsRequest = new HttpRequestMessage(HttpMethod.Get, $"{hub.ApiBaseUrl}/v1/events");
        eventsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
        using var eventsResponse = await hub.Http.SendAsync(
            eventsRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cts.Token);
        eventsResponse.EnsureSuccessStatusCode();
        await using var stream = await eventsResponse.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        (await hub.DeleteAsync($"/v1/satellites/{peer.PeerId}")).EnsureSuccessStatusCode();

        var sawEvent = false;
        while (!cts.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line == "event: satellite.revoked")
            {
                sawEvent = true;
                continue;
            }
            if (sawEvent && line?.StartsWith("data:", StringComparison.Ordinal) == true)
            {
                using var payload = JsonDocument.Parse(line["data:".Length..].Trim());
                Assert.Equal(peer.PeerId, payload.RootElement.GetProperty("data").GetProperty("peerId").GetString());
                return;
            }
        }

        Assert.Fail("The Hub published no satellite.revoked event.");
    }

    [Fact]
    public async Task SatelliteListener_DoesNotServeLocalApi()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var invite = await hub.CreateInviteAsync("Ann");
        var listener = new Uri(invite.Url).GetLeftPart(UriPartial.Authority);

        Assert.Equal(HttpStatusCode.NotFound, (await hub.Http.GetAsync($"{listener}/v1/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hub.Http.GetAsync($"{listener}/v1/appservers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hub.Http.GetAsync($"{listener}/v1/satellites")).StatusCode);
    }

    [Fact]
    public async Task Control_RejectsUnknownCredential()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);
        var invite = await hub.CreateInviteAsync("Ann");
        var listener = new Uri(invite.Url).Authority;

        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("Authorization", "Bearer inv_not_a_real_invite");
        await Assert.ThrowsAsync<WebSocketException>(async () => await socket.ConnectAsync(
            new Uri($"ws://{listener}/satellite/control?peer=sat_unknown"),
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, socket.HttpStatusCode);
    }

    [Fact]
    public async Task Bridge_ReturnsSatelliteOffline_WhenPeerNotConnected()
    {
        var paths = HubPaths.Resolve(_userProfile);
        Directory.CreateDirectory(paths.HubStatePath);
        var registry = new SatelliteRegistry(paths.SatellitesPath);
        var (peer, _) = registry.Pair("Ann", new SatelliteFrame { Kind = "hello", MachineName = "ANN-PC" });
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.SetRequestHeader("Authorization", "Bearer " + hub.Token);
        await socket.ConnectAsync(
            new Uri($"ws://{new Uri(hub.ApiBaseUrl).Authority}/v1/satellites/{peer.PeerId}/bridge?session=s1"),
            CancellationToken.None);
        var buffer = new byte[128];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal("satelliteOffline", socket.CloseStatusDescription);

        using var missing = new ClientWebSocket();
        missing.Options.CollectHttpResponseDetails = true;
        missing.Options.SetRequestHeader("Authorization", "Bearer " + hub.Token);
        await Assert.ThrowsAsync<WebSocketException>(async () => await missing.ConnectAsync(
            new Uri($"ws://{new Uri(hub.ApiBaseUrl).Authority}/v1/satellites/sat_missing/bridge?session=s2"),
            CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, missing.HttpStatusCode);
    }

    [Fact]
    public async Task Status_ReportsSatelliteCapability()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        using var document = JsonDocument.Parse(await hub.Http.GetStringAsync($"{hub.ApiBaseUrl}/v1/status"));

        Assert.True(document.RootElement.GetProperty("capabilities").GetProperty("satellites").GetBoolean());
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }
}

internal sealed class SatelliteHubFixture : IAsyncDisposable
{
    private readonly HubHost _host;
    private readonly Task _runTask;

    private SatelliteHubFixture(HubHost host, Task runTask, HubLockInfo info)
    {
        _host = host;
        _runTask = runTask;
        ApiBaseUrl = info.ApiBaseUrl;
        Token = info.Token;
        Http = new HttpClient();
    }

    public string ApiBaseUrl { get; }
    public string Token { get; }
    public HttpClient Http { get; }

    public static async Task<SatelliteHubFixture> StartAsync(string userProfile, int satellitePort = 0)
    {
        var paths = HubPaths.Resolve(userProfile);
        var host = new HubHost(
            new HubConfig { Port = 0, SatelliteHost = "127.0.0.1", SatellitePort = satellitePort },
            paths);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runTask = host.RunAsync(startup.Token);
        while (!startup.IsCancellationRequested)
        {
            if (runTask.IsFaulted)
                await runTask;
            var info = HubLockFile.TryRead(paths.LockFilePath);
            if (info is not null && !string.IsNullOrEmpty(info.Token))
                return new SatelliteHubFixture(host, runTask, info);
            await Task.Delay(50, startup.Token);
        }
        throw new TimeoutException("Hub did not publish its lock file.");
    }

    public async Task<CreatedInvite> CreateInviteAsync(string name, string? purpose = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/v1/satellites/invites")
        {
            Content = JsonContent.Create(new { name, host = "127.0.0.1", purpose })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CreatedInvite>()
            ?? throw new InvalidOperationException("The Hub returned no invitation.");
    }

    public async Task<HttpResponseMessage> DeleteAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return await Http.SendAsync(request);
    }

    public async Task<T> GetAsync<T>(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        using var response = await Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>()
            ?? throw new InvalidOperationException("The Hub returned an empty response.");
    }

    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        await _host.DisposeAsync();
        try { await _runTask; }
        catch (Exception) { }
    }

    public static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal sealed record CreatedInvite(string InviteId, string Url, DateTimeOffset ExpiresAt);
}
