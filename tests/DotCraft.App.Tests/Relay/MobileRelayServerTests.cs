using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using DotCraft.Relay;
using DotCraft.Tests.Hub;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace DotCraft.Tests.Relay;

public sealed class MobileRelayServerTests
{
    [Fact]
    public async Task Tunnel_SplicesThePhoneWithTheHostsAcceptedSocket()
    {
        await using var relay = await RelayFixture.StartAsync();
        using var host = await relay.ConnectAsync("/r/host?host=alpha", RelayFixture.Token);
        await relay.WaitForHostAsync("alpha");
        using var phone = await relay.ConnectAsync("/r/connect?host=alpha", token: null);
        using var tunnel = await relay.AcceptAsync(host);

        var upstream = RandomNumberGenerator.GetBytes(100_000);
        var downstream = RandomNumberGenerator.GetBytes(50_000);
        await phone.SendAsync(upstream, WebSocketMessageType.Binary, true, CancellationToken.None);
        await tunnel.SendAsync(downstream, WebSocketMessageType.Binary, true, CancellationToken.None);
        Assert.Equal(upstream, (await MobilePhone.ReceiveAsync(tunnel)).Payload);
        Assert.Equal(downstream, (await MobilePhone.ReceiveAsync(phone)).Payload);

        await phone.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Close, (await MobilePhone.ReceiveAsync(tunnel)).Type);
    }

    [Fact]
    public async Task HostRegistration_WithoutTheToken_IsRejected()
    {
        await using var relay = await RelayFixture.StartAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, await relay.RejectedStatusAsync("/r/host?host=alpha", token: null));
        Assert.Equal(HttpStatusCode.Unauthorized, await relay.RejectedStatusAsync("/r/host?host=alpha", "not-the-token"));
        await relay.AssertHostOfflineAsync("alpha");
    }

    [Fact]
    public async Task Connect_ToAnUnknownHost_AnswersHostOffline()
    {
        await using var relay = await RelayFixture.StartAsync();
        using var host = await relay.ConnectAsync("/r/host?host=alpha", RelayFixture.Token);
        await relay.WaitForHostAsync("alpha");

        await relay.AssertHostOfflineAsync("beta");
        Assert.Equal(HttpStatusCode.NotFound, await relay.RejectedStatusAsync("/r/connect?host=beta", token: null));
    }

    [Fact]
    public async Task NewerControlChannel_ReplacesTheOlder()
    {
        await using var relay = await RelayFixture.StartAsync();
        using var older = await relay.ConnectAsync("/r/host?host=alpha", RelayFixture.Token);
        await relay.WaitForHostAsync("alpha");
        using var newer = await relay.ConnectAsync("/r/host?host=alpha", RelayFixture.Token);

        var closed = await MobilePhone.ReceiveAsync(older);
        Assert.Equal(WebSocketMessageType.Close, closed.Type);
        Assert.Equal("replaced", closed.CloseDescription);

        using var phone = await relay.ConnectAsync("/r/connect?host=alpha", token: null);
        using var tunnel = await relay.AcceptAsync(newer);
        await phone.SendAsync("hello"u8.ToArray(), WebSocketMessageType.Binary, true, CancellationToken.None);
        Assert.Equal("hello", (await MobilePhone.ReceiveAsync(tunnel)).Text);
    }
}

internal sealed class RelayFixture : IAsyncDisposable
{
    public const string Token = "relay-test-token";

    private readonly WebApplication _app;
    private readonly HttpClient _http = new();

    private RelayFixture(WebApplication app, int port)
    {
        _app = app;
        Port = port;
    }

    public int Port { get; }

    public string Url => $"http://127.0.0.1:{Port}";

    public static async Task<RelayFixture> StartAsync(int? port = null)
    {
        var listenPort = port ?? SatelliteHubFixture.GetAvailablePort();
        var app = MobileRelayServer.Build($"http://127.0.0.1:{listenPort}", Token);
        await app.StartAsync();
        return new RelayFixture(app, listenPort);
    }

    public async Task<ClientWebSocket> ConnectAsync(string pathAndQuery, string? token)
    {
        var socket = new ClientWebSocket();
        if (token is not null)
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}{pathAndQuery}"), timeout.Token);
        return socket;
    }

    public async Task<HttpStatusCode> RejectedStatusAsync(string pathAndQuery, string? token)
    {
        using var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        if (token is not null)
            socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        await Assert.ThrowsAsync<WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}{pathAndQuery}"), CancellationToken.None));
        return socket.HttpStatusCode;
    }

    public async Task<ClientWebSocket> AcceptAsync(WebSocket host)
    {
        var open = JsonDocument.Parse((await MobilePhone.ReceiveAsync(host)).Payload).RootElement;
        Assert.Equal("open", open.GetProperty("type").GetString());
        return await ConnectAsync($"/r/accept?tunnel={Uri.EscapeDataString(open.GetProperty("tunnel").GetString()!)}", Token);
    }

    public async Task WaitForHostAsync(string hostId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            using (var response = await _http.GetAsync($"{Url}/r/connect?host={Uri.EscapeDataString(hostId)}", timeout.Token))
                if (response.StatusCode != HttpStatusCode.NotFound)
                    return;
            await Task.Delay(50, timeout.Token);
        }
    }

    public async Task AssertHostOfflineAsync(string hostId)
    {
        using var response = await _http.GetAsync($"{Url}/r/connect?host={Uri.EscapeDataString(hostId)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(MobileRelayServer.HostOffline, body.GetProperty("error").GetProperty("code").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
