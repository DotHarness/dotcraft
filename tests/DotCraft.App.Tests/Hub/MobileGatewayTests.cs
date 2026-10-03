using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class MobileGatewayTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubMobile_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Relay_PassesMessagesThroughUnchanged_UsingTheProcessToken()
    {
        await using var appServer = await StubAppServer.StartAsync();
        using var workspace = MobileHubFixture.RunWorkspace(_userProfile, "alpha", appServer.Endpoint);
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());
        await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
        using var phone = await hub.PairPhoneAsync();
        using var relay = await phone.ConnectAsync($"/m/projects/{await ProjectIdAsync(phone, "alpha")}/appserver");

        var text = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        var binary = RandomNumberGenerator.GetBytes(200_000);
        await relay.SendAsync(text, WebSocketMessageType.Text, true, CancellationToken.None);
        await relay.SendAsync(binary.AsMemory(0, 70_000), WebSocketMessageType.Binary, false, CancellationToken.None);
        await relay.SendAsync(binary.AsMemory(70_000, 70_000), WebSocketMessageType.Binary, false, CancellationToken.None);
        await relay.SendAsync(binary.AsMemory(140_000), WebSocketMessageType.Binary, true, CancellationToken.None);

        var receivedText = await ReadStubMessageAsync(appServer);
        var receivedBinary = await ReadStubMessageAsync(appServer);
        Assert.Equal(WebSocketMessageType.Text, receivedText.Type);
        Assert.Equal(text, receivedText.Payload);
        Assert.Equal(WebSocketMessageType.Binary, receivedBinary.Type);
        Assert.Equal(binary, receivedBinary.Payload);
        var echoedText = await MobilePhone.ReceiveAsync(relay);
        var echoedBinary = await MobilePhone.ReceiveAsync(relay);
        Assert.Equal(WebSocketMessageType.Text, echoedText.Type);
        Assert.Equal(text, echoedText.Payload);
        Assert.Equal(WebSocketMessageType.Binary, echoedBinary.Type);
        Assert.Equal(binary, echoedBinary.Payload);

        await relay.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await appServer.Closed.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Revoke_RequiresTheHubToken_ClosesThePhonesConnectionsWithinOneSecond_AndItsCredentialStopsWorking()
    {
        await using var appServer = await StubAppServer.StartAsync();
        using var workspace = MobileHubFixture.RunWorkspace(_userProfile, "alpha", appServer.Endpoint);
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());
        await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
        using var phone = await hub.PairPhoneAsync();
        using var relay = await phone.ConnectAsync($"/m/projects/{await ProjectIdAsync(phone, "alpha")}/appserver");
        using var events = await phone.ConnectAsync("/m/events");
        await relay.SendAsync("ping"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal("ping", (await MobilePhone.ReceiveAsync(relay)).Text);
        await AssertErrorAsync(
            await hub.SendAsync(HttpMethod.Delete, $"/v1/mobile/devices/{phone.DeviceId}", token: "not-the-token"),
            HttpStatusCode.Unauthorized,
            "unauthorized");

        var eventsReading = MobilePhone.ReadUntilClosedAsync(events);
        var relayReading = MobilePhone.ReadUntilClosedAsync(relay);
        var revoking = Stopwatch.StartNew();
        using (var revoked = await hub.SendAsync(HttpMethod.Delete, $"/v1/mobile/devices/{phone.DeviceId}"))
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        var eventMessages = await eventsReading;
        var relayMessages = await relayReading;
        revoking.Stop();

        Assert.Equal(["{\"type\":\"deviceRevoked\"}", ""], eventMessages.Select(message => message.Text));
        Assert.Equal("deviceRevoked", eventMessages[^1].CloseDescription);
        Assert.Equal("deviceRevoked", Assert.Single(relayMessages).CloseDescription);
        Assert.True(revoking.Elapsed < TimeSpan.FromSeconds(1), $"Revoke took {revoking.Elapsed}.");
        await appServer.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        await AssertErrorAsync(await phone.Http.GetAsync("/m/hello"), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task Disable_ClosesEveryConnection_StopsListening_AndKeepsPairedPhones()
    {
        await using var appServer = await StubAppServer.StartAsync();
        using var workspace = MobileHubFixture.RunWorkspace(_userProfile, "alpha", appServer.Endpoint);
        var port = SatelliteHubFixture.GetAvailablePort();
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, port);
        await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
        using var phone = await hub.PairPhoneAsync();
        using var relay = await phone.ConnectAsync($"/m/projects/{await ProjectIdAsync(phone, "alpha")}/appserver");
        using var events = await phone.ConnectAsync("/m/events");

        var eventsReading = MobilePhone.ReadUntilClosedAsync(events);
        var relayReading = MobilePhone.ReadUntilClosedAsync(relay);
        var disabling = Stopwatch.StartNew();
        var disabled = hub.JsonAsync(HttpMethod.Post, "/v1/mobile/disable");
        var eventMessages = await eventsReading;
        var relayMessages = await relayReading;
        disabling.Stop();
        var state = await disabled;

        Assert.Equal("off", state.GetProperty("state").GetString());
        Assert.Single(state.GetProperty("devices").EnumerateArray());
        Assert.Equal(["{\"type\":\"gatewayOff\"}", ""], eventMessages.Select(message => message.Text));
        Assert.Equal("gatewayOff", eventMessages[^1].CloseDescription);
        Assert.Equal("gatewayOff", Assert.Single(relayMessages).CloseDescription);
        Assert.True(disabling.Elapsed < TimeSpan.FromSeconds(1), $"Disable took {disabling.Elapsed}.");
        await appServer.Closed.WaitAsync(TimeSpan.FromSeconds(5));
        using var probe = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => probe.ConnectAsync(IPAddress.Loopback, port));
    }

    [Fact]
    public async Task Restart_KeepsPhoneAccessOnDevicesAndCertificate_WithoutStoringTheCredential()
    {
        var port = SatelliteHubFixture.GetAvailablePort();
        string fingerprint;
        string deviceId;
        string credential;
        await using (var hub = await MobileHubFixture.StartAsync(_userProfile, port))
        {
            await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
            using var phone = await hub.PairPhoneAsync();
            (fingerprint, deviceId, credential) = (phone.Fingerprint, phone.DeviceId, phone.Credential);
        }
        Assert.DoesNotContain(credential, File.ReadAllText(HubPaths.Resolve(_userProfile).MobilePath), StringComparison.Ordinal);

        await using var restarted = await MobileHubFixture.StartAsync(_userProfile, port);
        var state = await restarted.JsonAsync(HttpMethod.Get, "/v1/mobile");
        Assert.Equal("on", state.GetProperty("state").GetString());
        Assert.Equal(deviceId, Assert.Single(state.GetProperty("devices").EnumerateArray()).GetProperty("deviceId").GetString());

        using var samePhone = new MobilePhone(port, fingerprint);
        samePhone.UseCredential(deviceId, credential);
        await samePhone.GetJsonAsync("/m/hello");
    }

    private static async Task<string> ProjectIdAsync(MobilePhone phone, string displayName) =>
        (await phone.GetJsonAsync("/m/projects")).GetProperty("projects").EnumerateArray()
        .Single(project => project.GetProperty("displayName").GetString() == displayName)
        .GetProperty("projectId").GetString()!;

    private static async Task<(WebSocketMessageType Type, byte[] Payload)> ReadStubMessageAsync(StubAppServer appServer)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var payload = new MemoryStream();
        while (true)
        {
            var frame = await appServer.Frames.Reader.ReadAsync(timeout.Token);
            payload.Write(frame.Payload);
            if (frame.EndOfMessage)
                return (frame.Type, payload.ToArray());
        }
    }

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
