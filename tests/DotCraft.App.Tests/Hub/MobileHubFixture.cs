using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.AppServer;
using DotCraft.Hub;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.Hub;

internal sealed class MobileHubFixture : IAsyncDisposable
{
    private readonly HubHost _host;
    private readonly Task _runTask;
    private readonly HubLockInfo _info;
    private readonly HttpClient _http = new();

    private MobileHubFixture(HubHost host, Task runTask, HubLockInfo info)
    {
        _host = host;
        _runTask = runTask;
        _info = info;
    }

    public static async Task<MobileHubFixture> StartAsync(string userProfile, int mobilePort)
    {
        var paths = HubPaths.Resolve(userProfile);
        var host = new HubHost(
            new HubConfig { Port = 0, SatelliteHost = "127.0.0.1", MobileHost = "127.0.0.1", MobilePort = mobilePort },
            paths);
        using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var runTask = host.RunAsync(startup.Token);
        while (!startup.IsCancellationRequested)
        {
            if (runTask.IsFaulted)
                await runTask;
            var info = HubLockFile.TryRead(paths.LockFilePath);
            if (info is not null && !string.IsNullOrEmpty(info.Token))
                return new MobileHubFixture(host, runTask, info);
            await Task.Delay(50, startup.Token);
        }
        throw new TimeoutException("Hub did not publish its lock file.");
    }

    public static AppServerWorkspaceLock RunWorkspace(string userProfile, string name, Uri endpoint)
    {
        var workspace = Path.Combine(userProfile, "workspaces", name);
        var craft = Path.Combine(workspace, ".craft");
        Directory.CreateDirectory(craft);
        new HubAppServerRegistryStore(HubPaths.Resolve(userProfile).AppServersRegistryPath).Save([
            new HubAppServerRegistryRecord(
                WorkspacePath: workspace,
                CanonicalWorkspacePath: workspace,
                DisplayName: name,
                State: HubAppServerStates.Stopped,
                Pid: null,
                Endpoints: new Dictionary<string, string>(),
                ServiceStatus: new Dictionary<string, HubServiceStatus>(),
                ServerVersion: null,
                StartedByHub: true,
                LastStartedAt: null,
                LastSeenAt: null,
                LastExitedAt: null,
                ExitCode: null,
                LastError: null,
                RecentStderr: null)
        ]);
        Assert.True(AppServerWorkspaceLock.TryAcquire(new DotCraftPaths(workspace, craft, userDataPath: null), out var workspaceLock, out _));
        workspaceLock!.Publish(new AppServerLockInfo(
            Pid: Environment.ProcessId,
            WorkspacePath: workspace,
            ManagedByHub: true,
            HubApiBaseUrl: "http://127.0.0.1:43000",
            StartedAt: DateTimeOffset.UtcNow,
            Version: "test",
            Endpoints: new Dictionary<string, string> { ["appServerWebSocket"] = endpoint.ToString() }));
        return workspaceLock;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, $"{_info.ApiBaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? _info.Token);
        if (body is not null || method == HttpMethod.Post)
            request.Content = JsonContent.Create(body ?? new { });
        return await _http.SendAsync(request);
    }

    public async Task<JsonElement> JsonAsync(HttpMethod method, string path, object? content = null)
    {
        using var response = await SendAsync(method, path, body: content);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task<MobilePhone> PairPhoneAsync()
    {
        var qrPayload = (await JsonAsync(HttpMethod.Post, "/v1/mobile/pairings")).GetProperty("qrPayload").GetString()!;
        var query = QueryHelpers.ParseQuery(new Uri(qrPayload).Query);
        var phone = new MobilePhone(int.Parse(query["port"].ToString()), query["fp"].ToString());
        using var response = await phone.Http.PostAsJsonAsync(
            "/m/pair",
            new { code = query["code"].ToString(), displayName = "Test phone", platform = "android" });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var paired = JsonDocument.Parse(body).RootElement;
        phone.UseCredential(paired.GetProperty("deviceId").GetString()!, paired.GetProperty("credential").GetString()!);
        return phone;
    }

    public async ValueTask DisposeAsync()
    {
        _http.Dispose();
        await _host.DisposeAsync();
        try { await _runTask; }
        catch (Exception) { }
    }
}

internal sealed class MobilePhone : IDisposable
{
    private readonly int _port;

    public MobilePhone(int port, string fingerprint)
    {
        _port = port;
        Fingerprint = fingerprint;
        Http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, certificate, _, _) => IsPinned(certificate) })
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}")
        };
    }

    public string Fingerprint { get; }
    public HttpClient Http { get; }
    public string DeviceId { get; private set; } = string.Empty;
    public string Credential { get; private set; } = string.Empty;

    public void UseCredential(string deviceId, string credential)
    {
        DeviceId = deviceId;
        Credential = credential;
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential);
    }

    public async Task<ClientWebSocket> ConnectAsync(string path)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) => IsPinned(certificate);
        socket.Options.SetRequestHeader("Authorization", "Bearer " + Credential);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await socket.ConnectAsync(new Uri($"wss://127.0.0.1:{_port}{path}"), timeout.Token);
        return socket;
    }

    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await Http.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public static async Task<ReceivedMessage> ReceiveAsync(WebSocket socket)
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        using var payload = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancel.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancel.Token);
                return new ReceivedMessage(WebSocketMessageType.Close, [], socket.CloseStatusDescription);
            }
            payload.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return new ReceivedMessage(result.MessageType, payload.ToArray(), null);
        }
    }

    public static async Task<IReadOnlyList<ReceivedMessage>> ReadUntilClosedAsync(WebSocket socket)
    {
        var messages = new List<ReceivedMessage>();
        while (true)
        {
            var message = await ReceiveAsync(socket);
            messages.Add(message);
            if (message.Type == WebSocketMessageType.Close)
                return messages;
        }
    }

    public void Dispose() => Http.Dispose();

    private bool IsPinned(X509Certificate? certificate) =>
        certificate is not null
        && string.Equals(Convert.ToHexStringLower(SHA256.HashData(certificate.GetRawCertData())), Fingerprint, StringComparison.Ordinal);

    internal sealed record ReceivedMessage(WebSocketMessageType Type, byte[] Payload, string? CloseDescription)
    {
        public string Text => Encoding.UTF8.GetString(Payload);
    }
}

internal sealed class StubAppServer : IAsyncDisposable
{
    private const string ProcessToken = "stub-process-token";

    private readonly WebApplication _app;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private StubAppServer(WebApplication app, int port)
    {
        _app = app;
        Endpoint = new Uri($"ws://127.0.0.1:{port}/ws?token={ProcessToken}");
    }

    public Uri Endpoint { get; }
    public Channel<(WebSocketMessageType Type, byte[] Payload, bool EndOfMessage)> Frames { get; } =
        Channel.CreateUnbounded<(WebSocketMessageType, byte[], bool)>();
    public Task Closed => _closed.Task;

    public static async Task<StubAppServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.UseWebSockets();
        var port = SatelliteHubFixture.GetAvailablePort();
        StubAppServer? stub = null;
        app.Map("/ws", async (HttpContext context) =>
        {
            if (context.Request.Query["token"] != ProcessToken)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await stub!.EchoAsync(socket);
        });
        app.Urls.Add($"http://127.0.0.1:{port}");
        await app.StartAsync();
        stub = new StubAppServer(app, port);
        return stub;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task EchoAsync(WebSocket socket)
    {
        var buffer = new byte[256 * 1024];
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                var payload = buffer[..result.Count];
                Frames.Writer.TryWrite((result.MessageType, payload, result.EndOfMessage));
                await socket.SendAsync(payload, result.MessageType, result.EndOfMessage, CancellationToken.None);
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _closed.TrySetResult();
        }
    }
}
