using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Tests.Relay;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class MobileRelayHubTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubMobileRelay_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PairedPhone_ReachesTheGatewayThroughTheRelay_WithThePinnedCertificate()
    {
        await using var relay = await RelayFixture.StartAsync();
        await using var appServer = await StubAppServer.StartAsync();
        using var workspace = MobileHubFixture.RunWorkspace(_userProfile, "alpha", appServer.Endpoint);
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());
        await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
        using var phone = await hub.PairPhoneAsync();
        var projectId = (await phone.GetJsonAsync("/m/projects")).GetProperty("projects").EnumerateArray()
            .Single(project => project.GetProperty("displayName").GetString() == "alpha")
            .GetProperty("projectId").GetString();

        var state = await hub.JsonAsync(HttpMethod.Put, "/v1/mobile/relay", new { url = relay.Url, token = RelayFixture.Token });
        Assert.Equal(relay.Url, state.GetProperty("relay").GetProperty("url").GetString());
        Assert.DoesNotContain(RelayFixture.Token, state.GetRawText(), StringComparison.Ordinal);
        await WaitForRelayStateAsync(hub, "connected");
        var hostId = (await phone.GetJsonAsync("/m/hello")).GetProperty("relay").GetProperty("hostId").GetString()!;
        var pairing = QueryHelpers.ParseQuery(new Uri(
            (await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/pairings")).GetProperty("qrPayload").GetString()!).Query);
        Assert.Equal(relay.Url, pairing["relay"].ToString());
        Assert.Equal(hostId, pairing["host"].ToString());

        await relay.WaitForHostAsync(hostId);
        using var tunneled = ThroughRelay(relay, hostId, phone);
        var hello = await tunneled.GetJsonAsync("/m/hello");
        Assert.Equal(phone.Fingerprint, hello.GetProperty("fingerprint").GetString());

        using var appServerSocket = new ClientWebSocket();
        appServerSocket.Options.SetRequestHeader("Authorization", "Bearer " + phone.Credential);
        await appServerSocket.ConnectAsync(
            new Uri($"wss://gateway.test/m/projects/{projectId}/appserver"),
            new HttpMessageInvoker(tunneled.Handler, disposeHandler: false),
            CancellationToken.None);
        await appServerSocket.SendAsync("ping"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal("ping", (await MobilePhone.ReceiveAsync(appServerSocket)).Text);
    }

    [Fact]
    public async Task Hub_ReconnectsItsControlChannel_AfterTheRelayRestarts()
    {
        var relayPort = SatelliteHubFixture.GetAvailablePort();
        var relay = await RelayFixture.StartAsync(relayPort);
        await using var hub = await MobileHubFixture.StartAsync(_userProfile, SatelliteHubFixture.GetAvailablePort());
        await hub.JsonAsync(HttpMethod.Post, "/v1/mobile/enable");
        using var phone = await hub.PairPhoneAsync();
        await hub.JsonAsync(HttpMethod.Put, "/v1/mobile/relay", new { url = relay.Url, token = RelayFixture.Token });
        await WaitForRelayStateAsync(hub, "connected");
        var hostId = (await phone.GetJsonAsync("/m/hello")).GetProperty("relay").GetProperty("hostId").GetString()!;

        await relay.DisposeAsync();
        await WaitForRelayStateAsync(hub, "connecting", "failed");
        await using var restarted = await RelayFixture.StartAsync(relayPort);
        await WaitForRelayStateAsync(hub, "connected");

        await restarted.WaitForHostAsync(hostId);
        using var tunneled = ThroughRelay(restarted, hostId, phone);
        Assert.Equal(phone.Fingerprint, (await tunneled.GetJsonAsync("/m/hello")).GetProperty("fingerprint").GetString());
    }

    private static RelayedPhone ThroughRelay(RelayFixture relay, string hostId, MobilePhone phone)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new ClientWebSocket();
                await socket.ConnectAsync(
                    new Uri($"ws://127.0.0.1:{relay.Port}/r/connect?host={Uri.EscapeDataString(hostId)}"),
                    cancellationToken);
                return WebSocketStream.Create(socket, WebSocketMessageType.Binary, ownsWebSocket: true);
            },
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null
                    && Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData())) == phone.Fingerprint
            }
        };
        var http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri("https://gateway.test") };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", phone.Credential);
        return new RelayedPhone(handler, http);
    }

    private static async Task WaitForRelayStateAsync(MobileHubFixture hub, params string[] states)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        string? state = null;
        while (!timeout.IsCancellationRequested)
        {
            state = (await hub.JsonAsync(HttpMethod.Get, "/v1/mobile")).GetProperty("relay").GetProperty("state").GetString();
            if (states.Contains(state))
                return;
            await Task.Delay(100);
        }
        Assert.Fail($"Relay state stayed '{state}', expected one of {string.Join(", ", states)}.");
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }

    private sealed class RelayedPhone(SocketsHttpHandler handler, HttpClient http) : IDisposable
    {
        public SocketsHttpHandler Handler => handler;

        public async Task<JsonElement> GetJsonAsync(string path)
        {
            using var response = await http.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        public void Dispose() => http.Dispose();
    }
}
