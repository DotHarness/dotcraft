using System.Net;
using System.Text.Json;
using DotCraft.Sdk.Hub;

namespace DotCraft.Sdk.Tests;

public sealed class HubClientTests
{
    [Fact]
    public async Task GetAppServerByWorkspaceAsync_ReadsLockAndUsesBearerToken()
    {
        using var temp = new TemporaryDirectory();
        var lockPath = Path.Combine(temp.Path, "hub.lock");
        var pid = Environment.ProcessId;
        await File.WriteAllTextAsync(lockPath, $$"""
            {
              "pid": {{pid}},
              "apiBaseUrl": "http://127.0.0.1:49123",
              "token": "hub-token"
            }
            """);

        var handler = new CaptureHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"hubVersion":"test","pid":1,"startedAt":"2026-05-18T00:00:00Z","statePath":"","apiBaseUrl":"http://127.0.0.1:49123","capabilities":{}}""")
                };
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("hub-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("/v1/appservers/by-workspace", request.RequestUri?.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "workspacePath": "E:/workspace",
                      "canonicalWorkspacePath": "E:/workspace",
                      "state": "running",
                      "pid": 123,
                      "endpoints": { "appServerWebSocket": "ws://127.0.0.1:5000/ws?token=x" },
                      "serviceStatus": {},
                      "serverVersion": "0.1",
                      "startedByHub": true
                    }
                    """)
            };
        });
        var hub = new HubClient(new DotCraftHubClientOptions
        {
            HubLockPath = lockPath,
            StartHubIfMissing = false,
            HttpClientFactory = () => new HttpClient(handler)
        });

        var response = await hub.GetAppServerByWorkspaceAsync("E:/workspace");

        Assert.NotNull(response);
        Assert.Equal(HubAppServerStates.Running, response.State);
        Assert.Equal("ws://127.0.0.1:5000/ws?token=x", response.Endpoints["appServerWebSocket"]);
    }

    [Fact]
    public void ParseHubBaseUrl_RejectsNonLoopbackHosts()
    {
        var ex = Assert.Throws<HubClientException>(() => HubClient.ParseHubBaseUrl("http://example.com:1234"));
        Assert.Equal("invalidHubLock", ex.Code);
    }

    [Fact]
    public async Task EnsureDefaultChatAppServerAsync_CreatesWorkspaceAndUsesExistingEnsureEndpoint()
    {
        using var temp = new TemporaryDirectory();
        var hubDir = Path.Combine(temp.Path, ".craft", "hub");
        Directory.CreateDirectory(hubDir);
        await File.WriteAllTextAsync(Path.Combine(hubDir, "hub.lock"), $$"""
            {
              "pid": {{Environment.ProcessId}},
              "apiBaseUrl": "http://127.0.0.1:49124",
              "token": "hub-token"
            }
            """);

        string? capturedWorkspace = null;
        var handler = new CaptureHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/status")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"hubVersion":"test","pid":1,"startedAt":"2026-05-18T00:00:00Z","statePath":"","apiBaseUrl":"http://127.0.0.1:49124","capabilities":{}}""")
                };
            }

            Assert.Equal("/v1/appservers/ensure", request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("hub-token", request.Headers.Authorization?.Parameter);
            using var body = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            capturedWorkspace = body.RootElement.GetProperty("workspacePath").GetString();
            var responseBody = JsonSerializer.Serialize(new
            {
                workspacePath = capturedWorkspace,
                canonicalWorkspacePath = capturedWorkspace,
                state = HubAppServerStates.Running,
                pid = 123,
                endpoints = new Dictionary<string, string>
                {
                    ["appServerWebSocket"] = "ws://127.0.0.1:5000/ws?token=x"
                },
                serviceStatus = new Dictionary<string, object>(),
                serverVersion = "0.1",
                startedByHub = true,
                exitCode = (int?)null,
                lastError = (string?)null,
                recentStderr = (string?)null
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        });
        var hub = new HubClient(new DotCraftHubClientOptions
        {
            UserProfilePath = temp.Path,
            StartHubIfMissing = false,
            HttpClientFactory = () => new HttpClient(handler)
        });

        var response = await hub.EnsureDefaultChatAppServerAsync();
        var expectedWorkspace = Path.GetFullPath(Path.Combine(temp.Path, ".craft", "workspaces", "chats"));

        Assert.Equal(expectedWorkspace, capturedWorkspace);
        Assert.Equal(expectedWorkspace, response.WorkspacePath);
        Assert.True(Directory.Exists(Path.Combine(expectedWorkspace, ".craft", "memory")));
        Assert.Equal("{}" + Environment.NewLine, await File.ReadAllTextAsync(Path.Combine(expectedWorkspace, ".craft", "config.json")));
    }

    private sealed class CaptureHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handle(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dotcraft-sdk-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
