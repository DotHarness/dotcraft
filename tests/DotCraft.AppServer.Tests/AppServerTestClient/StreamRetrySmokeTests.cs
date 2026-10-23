using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DotCraft.AppServerTestClient;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.AppServerTestClient;

public sealed class StreamRetrySmokeTests
{
    [Fact]
    public void BuildConfigJson_MergesWithGlobalProviderCredentials()
    {
        var root = Path.Combine(Path.GetTempPath(), "stream-retry-merge-" + Guid.NewGuid().ToString("N"));
        var globalPath = Path.Combine(root, "global", ".craft", "config.json");
        var workspacePath = Path.Combine(root, "workspace", ".craft", "config.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(globalPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(workspacePath)!);
            File.WriteAllText(globalPath, """
                {
                  "ProviderId": "openai-chat",
                  "ProviderPreferences": {
                    "openai-chat": {
                      "Model": "global-model"
                    }
                  },
                  "Providers": {
                    "openai-chat": {
                      "Protocol": "openai-chat-completions",
                      "ApiKey": "sk-test",
                      "EndPoint": "https://api.openai.com/v1",
                      "MaxOutputTokens": 123
                    }
                  }
                }
                """);
            var workspaceJson = StreamRetrySmokeWorkspace.BuildConfigJson(
                "openai-chat",
                "gpt-test",
                new Uri("http://127.0.0.1:54321"));
            File.WriteAllText(workspacePath, workspaceJson);
            using (var doc = JsonDocument.Parse(workspaceJson))
            {
                var workspace = doc.RootElement;
                Assert.Empty(workspace.GetProperty("McpServers").EnumerateObject());
                Assert.Empty(workspace.GetProperty("LspServers").EnumerateObject());
                Assert.Equal(0, workspace.GetProperty("ExternalChannels").GetArrayLength());
                var workspaceProvider = workspace.GetProperty("Providers").GetProperty("openai-chat");
                Assert.False(workspaceProvider.TryGetProperty("ApiKey", out _));
                Assert.False(workspaceProvider.TryGetProperty("Protocol", out _));
            }

            var config = AppConfig.LoadWithGlobalFallback(workspacePath, globalPath);
            var provider = config.Providers["openai-chat"];

            Assert.Equal("gpt-test", config.ProviderPreferences["openai-chat"].Model);
            Assert.Equal("openai-chat", config.ProviderId);
            Assert.Equal(ModelProviderProtocols.OpenAIChatCompletions, provider.Protocol);
            Assert.Equal("sk-test", provider.ApiKey);
            Assert.Equal("http://127.0.0.1:54321", provider.EndPoint);
            Assert.Equal(123, provider.MaxOutputTokens);
            Assert.Equal(1, provider.StreamMaxRetries);
            Assert.Equal(10_000, provider.StreamIdleTimeoutMs);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public async Task FaultProxy_FaultsFirstStreamingPostAndForwardsSecond()
    {
        await using var upstream = await TestUpstreamServer.StartAsync();
        await using var proxy = await StreamRetrySmokeFaultProxy.StartAsync(
            new Uri(upstream.Endpoint, "/v1"));

        using (var firstHttp = new HttpClient())
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                using var first = await firstHttp.PostAsync(
                    new Uri(proxy.Endpoint, "/chat/completions"),
                    JsonContent(),
                    cts.Token);
                await first.Content.ReadAsStringAsync(cts.Token);
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }
            catch (IOException)
            {
            }
        }

        // Use a fresh connection pool after the intentional reset. Reusing the faulted
        // Windows HTTP connection can surface the prior reset on this independent request.
        using var secondHttp = new HttpClient();
        using var second = await secondHttp.PostAsync(
            new Uri(proxy.Endpoint, "/chat/completions"),
            JsonContent());
        var body = await second.Content.ReadAsStringAsync();

        var snapshot = await WaitForProxySnapshotAsync(proxy);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("data: ok", body);
        Assert.Equal(1, upstream.RequestCount);
        Assert.Equal("/v1/chat/completions", upstream.LastPath);
        Assert.Equal(1, snapshot.FaultedRequests);
        Assert.Equal(1, snapshot.ForwardedRequests);
        Assert.Collection(
            snapshot.Requests,
            faulted => Assert.Equal("faulted", faulted.Kind),
            forwarded =>
            {
                Assert.Equal("forwarded", forwarded.Kind);
                Assert.Equal(200, forwarded.UpstreamStatusCode);
            });
    }

    private static StringContent JsonContent() =>
        new("""{"stream":true}""", Encoding.UTF8, "application/json");

    private static async Task<StreamRetrySmokeProxySnapshot> WaitForProxySnapshotAsync(
        StreamRetrySmokeFaultProxy proxy)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var snapshot = proxy.Snapshot();
            if (snapshot.FaultedRequests == 1 && snapshot.ForwardedRequests == 1 && snapshot.Requests.Count == 2)
                return snapshot;

            await Task.Delay(50);
        }

        return proxy.Snapshot();
    }

    private sealed class TestUpstreamServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private int _requestCount;

        private TestUpstreamServer(WebApplication app, Uri endpoint)
        {
            _app = app;
            Endpoint = endpoint;
        }

        public Uri Endpoint { get; }

        public int RequestCount => Volatile.Read(ref _requestCount);

        public string? LastPath { get; private set; }

        public static async Task<TestUpstreamServer> StartAsync()
        {
            var port = AllocateLoopbackPort();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));

            var app = builder.Build();
            var server = new TestUpstreamServer(app, new Uri($"http://127.0.0.1:{port}"));
            app.Run(async context =>
            {
                Interlocked.Increment(ref server._requestCount);
                server.LastPath = context.Request.Path.Value;
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync("data: ok\n\n");
            });
            await app.StartAsync();
            return server;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _app.StopAsync(cts.Token);
            }
            finally
            {
                await _app.DisposeAsync();
            }
        }

        private static int AllocateLoopbackPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
