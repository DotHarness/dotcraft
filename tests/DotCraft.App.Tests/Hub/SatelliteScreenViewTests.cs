using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotCraft.Hub;
using DotCraft.Protocol.ScreenView;
using DotCraft.RemoteTools;
using DotCraft.Screen;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class SatelliteScreenViewTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftSatelliteScreen_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScreenView_StreamsWhileWatched_AndCountsAsConnectedWithoutALease()
    {
        var capture = new FakeScreenCaptureSource();
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, screenCapture: () => capture);
        var peer = (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Single();
        Assert.Contains(SatelliteWire.ScreenCapability, peer.Capabilities);
        Assert.Equal(RemoteToolHostStatus.Standby, scenario.Runtime.Status);

        using var viewer = await OpenViewAsync(scenario);
        Assert.Equal(
            new ScreenViewCapability(true, null),
            JsonSerializer.Deserialize<ScreenViewCapability>(await ReceiveTextAsync(viewer), Json));

        await SendControlAsync(viewer, new ScreenViewControl(1, 8, 640, 82));
        var frame = await ReceiveBinaryAsync(viewer);
        Assert.True(ScreenViewProtocol.TryReadFrameHeader(frame, out var header));
        Assert.Equal((640, 360), ((int)header.Width, (int)header.Height));
        Assert.Equal(FakeScreenCaptureSource.Jpeg, frame[ScreenViewProtocol.FrameHeaderBytes..]);
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(() =>
            Task.FromResult(scenario.Runtime.Status == RemoteToolHostStatus.Connected));
        Assert.Equal(1, scenario.Runtime.Peers.Single().ScreenViewers);

        await SendControlAsync(viewer, new ScreenViewControl(0, 8, 640, 82));
        await Task.Delay(400);
        var captured = capture.Captures;
        await Task.Delay(400);
        Assert.Equal(captured, capture.Captures);

        await viewer.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        await SatelliteBridgeEndToEndTests.WaitUntilAsync(() =>
            Task.FromResult(scenario.Runtime.Status == RemoteToolHostStatus.Standby));
        Assert.Equal(0, scenario.Runtime.Peers.Single().ScreenViewers);
    }

    [Fact]
    public async Task ScreenView_EndsWithSharingPaused_AndIsRefusedWhilePaused()
    {
        var capture = new FakeScreenCaptureSource();
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, screenCapture: () => capture);
        using var viewer = await OpenViewAsync(scenario);
        await ReceiveTextAsync(viewer);
        await SendControlAsync(viewer, new ScreenViewControl(1, 8, 640, 82));
        await ReceiveBinaryAsync(viewer);

        await scenario.Runtime.SetSharingPausedAsync(true);

        await ReceiveUntilClosedAsync(viewer);
        Assert.Equal(SatelliteWire.ScreenClosedPaused, viewer.CloseStatusDescription);

        using var refused = await OpenViewAsync(scenario);
        await ReceiveUntilClosedAsync(refused);
        Assert.Equal(SatelliteWire.ScreenClosedPaused, refused.CloseStatusDescription);
    }

    [Fact]
    public async Task Bridge_RefusesAScreenView_WhenThePeerDoesNotShareItsScreen()
    {
        await using var scenario = await SatelliteScenario.StartAsync(
            _userProfile,
            screenCapture: () => new UnavailableScreenCaptureSource(ScreenCaptureReasons.NoCaptureBackend));
        var peer = (await scenario.Hub.GetAsync<HubSatelliteResponse[]>("/v1/satellites")).Single();
        Assert.Empty(peer.Capabilities);

        using var response = await scenario.Hub.Http.SendAsync(BridgeRequest(scenario.Hub, scenario.PeerId, "screen"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("satelliteScreenUnsupported", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScreenView_SaysNothing_WhenASingleFrameFails()
    {
        var capture = new FakeScreenCaptureSource();
        capture.FailNext(1, ScreenCaptureReasons.CaptureFailed, "BitBlt failed (Win32 6)");
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, screenCapture: () => capture);
        using var viewer = await OpenViewAsync(scenario);
        Assert.Equal(new ScreenViewCapability(true, null), await ReceiveCapabilityAsync(viewer));

        await SendControlAsync(viewer, new ScreenViewControl(1, 8, 640, 82));

        var (type, payload) = await ReceiveMessageAsync(viewer);
        Assert.Equal(WebSocketMessageType.Binary, type);
        Assert.True(ScreenViewProtocol.TryReadFrameHeader(payload, out _));
    }

    [Fact]
    public async Task ScreenView_ReportsCaptureFailedWithDetailOnce_ThenClearsItBeforeTheFrameThatRecovers()
    {
        var capture = new FakeScreenCaptureSource();
        capture.FailNext(3, ScreenCaptureReasons.CaptureFailed, "BitBlt failed (Win32 6)");
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, screenCapture: () => capture);
        using var viewer = await OpenViewAsync(scenario);
        await ReceiveCapabilityAsync(viewer);

        await SendControlAsync(viewer, new ScreenViewControl(1, 8, 640, 82));

        Assert.Equal(
            new ScreenViewCapability(true, ScreenCaptureReasons.CaptureFailed, "BitBlt failed (Win32 6)"),
            await ReceiveCapabilityAsync(viewer));
        Assert.Equal(new ScreenViewCapability(true, null), await ReceiveCapabilityAsync(viewer));

        var (type, payload) = await ReceiveMessageAsync(viewer);
        Assert.Equal(WebSocketMessageType.Binary, type);
        Assert.True(ScreenViewProtocol.TryReadFrameHeader(payload, out _));
    }

    [Fact]
    public async Task ScreenView_HalvesTheWidth_WhenTheQualityDropStillExceedsTheBudget()
    {
        var capture = new FakeScreenCaptureSource
        {
            Encode = request => request.MaxWidth <= 320 && request.Quality <= 40
                ? FakeScreenCaptureSource.Jpeg
                : new byte[ScreenViewProtocol.MaximumFrameBytes + 1]
        };
        await using var scenario = await SatelliteScenario.StartAsync(_userProfile, screenCapture: () => capture);
        using var viewer = await OpenViewAsync(scenario);
        await ReceiveCapabilityAsync(viewer);

        await SendControlAsync(viewer, new ScreenViewControl(1, 8, 640, 82));

        var frame = await ReceiveBinaryAsync(viewer);
        Assert.True(ScreenViewProtocol.TryReadFrameHeader(frame, out var header));
        Assert.Equal((320, 180), ((int)header.Width, (int)header.Height));
        Assert.Equal(FakeScreenCaptureSource.Jpeg, frame[ScreenViewProtocol.FrameHeaderBytes..]);
    }

    [Fact]
    public async Task Bridge_RefusesAScreenView_WhenThePeerIsEnrolledButOffline()
    {
        var paths = HubPaths.Resolve(_userProfile);
        Directory.CreateDirectory(paths.HubStatePath);
        var registry = new SatelliteRegistry(paths.SatellitesPath);
        var (peer, _) = registry.Pair("Ann", new SatelliteFrame { Kind = "hello", MachineName = "ANN-PC" });
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        using var response = await hub.Http.SendAsync(BridgeRequest(hub, peer.PeerId, "screen"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("satelliteOffline", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bridge_RejectsAnUnknownSessionKind()
    {
        await using var hub = await SatelliteHubFixture.StartAsync(_userProfile);

        using var response = await hub.Http.SendAsync(BridgeRequest(hub, "sat_any", "audio"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sessionKindUnsupported", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_userProfile, recursive: true); }
        catch (Exception) { }
    }

    private static HttpRequestMessage BridgeRequest(SatelliteHubFixture hub, string peerId, string kind)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{hub.ApiBaseUrl}/v1/satellites/{peerId}/bridge?session={Guid.NewGuid():N}&kind={kind}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
        return request;
    }

    private static async Task<ClientWebSocket> OpenViewAsync(SatelliteScenario scenario)
    {
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + scenario.Hub.Token);
        var uri = new UriBuilder(scenario.Hub.ApiBaseUrl)
        {
            Scheme = "ws",
            Path = $"/v1/satellites/{scenario.PeerId}/bridge",
            Query = $"session={Guid.NewGuid():N}&kind={SatelliteWire.SessionKindScreen}"
        }.Uri;
        await socket.ConnectAsync(uri, new CancellationTokenSource(Timeout).Token);
        return socket;
    }

    private static Task SendControlAsync(WebSocket socket, ScreenViewControl control) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(control, Json),
            WebSocketMessageType.Text,
            true,
            new CancellationTokenSource(Timeout).Token);

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        var (type, payload) = await ReceiveMessageAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, type);
        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<ScreenViewCapability> ReceiveCapabilityAsync(WebSocket socket) =>
        JsonSerializer.Deserialize<ScreenViewCapability>(await ReceiveTextAsync(socket), Json)!;

    private static async Task<byte[]> ReceiveBinaryAsync(WebSocket socket)
    {
        while (true)
        {
            var (type, payload) = await ReceiveMessageAsync(socket);
            Assert.NotEqual(WebSocketMessageType.Close, type);
            if (type == WebSocketMessageType.Binary)
                return payload;
        }
    }

    private static async Task ReceiveUntilClosedAsync(WebSocket socket)
    {
        while ((await ReceiveMessageAsync(socket)).Type != WebSocketMessageType.Close)
        {
        }
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    private static async Task<(WebSocketMessageType Type, byte[] Payload)> ReceiveMessageAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        var buffer = new byte[64 * 1024];
        var payload = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                return (result.MessageType, []);
            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return (result.MessageType, payload.ToArray());
        }
    }

    private sealed class FakeScreenCaptureSource : IScreenCaptureSource
    {
        public static readonly byte[] Jpeg = [.. Enumerable.Range(0, 3000).Select(index => (byte)index)];

        private readonly ConcurrentQueue<ScreenCaptureResult> _scripted = new();
        private int _captures;

        public Func<ScreenCaptureRequest, byte[]> Encode { get; init; } = _ => Jpeg;

        public int Captures => Volatile.Read(ref _captures);

        public void FailNext(int times, string reason, string? detail)
        {
            for (var index = 0; index < times; index++)
                _scripted.Enqueue(ScreenCaptureResult.Unavailable(reason, detail));
        }

        public ScreenCaptureCapability Probe() => new(true, null);

        public ScreenCaptureResult Capture(ScreenCaptureRequest request)
        {
            Interlocked.Increment(ref _captures);
            if (_scripted.TryDequeue(out var scripted))
                return scripted;
            var (width, height) = ScreenCaptureSource.Fit(1920, 1080, request.MaxWidth);
            return ScreenCaptureResult.Captured(new ScreenFrame(width, height, Encode(request)));
        }

        public void Dispose()
        {
        }
    }
}
