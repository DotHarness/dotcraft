using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DotCraft.Hub;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubHostTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubHost_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_StatusAndShutdownEndpointsWork()
    {
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);
        Assert.False(string.IsNullOrWhiteSpace(info.BinaryPath));

        using var http = new HttpClient();
        var status = await http.GetStringAsync($"{info.ApiBaseUrl}/v1/status", cts.Token);
        using var statusDoc = JsonDocument.Parse(status);
        var root = statusDoc.RootElement;

        Assert.Equal(Environment.ProcessId, root.GetProperty("pid").GetInt32());
        Assert.Equal(paths.HubStatePath, root.GetProperty("statePath").GetString());
        Assert.Equal(info.BinaryPath, root.GetProperty("binaryPath").GetString());
        Assert.True(root.GetProperty("capabilities").GetProperty("appServerManagement").GetBoolean());
        Assert.True(root.GetProperty("capabilities").GetProperty("portManagement").GetBoolean());
        Assert.True(root.GetProperty("capabilities").GetProperty("events").GetBoolean());
        Assert.True(root.GetProperty("capabilities").GetProperty("notifications").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("tray").GetBoolean());

        var unauthorized = await http.PostAsync($"{info.ApiBaseUrl}/v1/shutdown", content: null, cts.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{info.ApiBaseUrl}/v1/shutdown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        var shutdown = await http.SendAsync(request, cts.Token);
        Assert.True(shutdown.IsSuccessStatusCode);

        await runTask.WaitAsync(cts.Token);
        Assert.False(File.Exists(paths.LockFilePath));
    }

    [Fact]
    public async Task RunAsync_ProtectedEndpointsRequireToken()
    {
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);

        using var http = new HttpClient();
        var appservers = await http.GetAsync($"{info.ApiBaseUrl}/v1/appservers", cts.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, appservers.StatusCode);

        var notify = await http.PostAsJsonAsync($"{info.ApiBaseUrl}/v1/notifications/request", new
        {
            kind = "turnCompleted",
            title = "Done"
        }, cts.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, notify.StatusCode);

        var events = await http.GetAsync($"{info.ApiBaseUrl}/v1/events", cts.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, events.StatusCode);

        await ShutdownAsync(http, info, cts.Token);
        await runTask.WaitAsync(cts.Token);
    }

    [Fact]
    public async Task RunAsync_UnexpectedEndpointErrorsReturnHubErrorResponse()
    {
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);

        using var http = new HttpClient();
        using var request = AuthorizedPost(info, $"{info.ApiBaseUrl}/v1/appservers/ensure", new
        {
            workspacePath = "\0",
            startIfMissing = false
        });

        var response = await http.SendAsync(request, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        using var doc = JsonDocument.Parse(body);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal("hubInternalError", error.GetProperty("code").GetString());
        Assert.Equal("Hub encountered an unexpected internal error.", error.GetProperty("message").GetString());

        await ShutdownAsync(http, info, cts.Token);
        await runTask.WaitAsync(cts.Token);
    }

    [Fact]
    public async Task RunAsync_NotificationRequestEmitsSseEvent()
    {
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);

        using var http = new HttpClient();
        {
            using var eventsRequest = new HttpRequestMessage(HttpMethod.Get, $"{info.ApiBaseUrl}/v1/events");
            eventsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            using var eventsResponse = await http.SendAsync(eventsRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            eventsResponse.EnsureSuccessStatusCode();

            await using var stream = await eventsResponse.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);

            using var notifyRequest = new HttpRequestMessage(HttpMethod.Post, $"{info.ApiBaseUrl}/v1/notifications/request")
            {
                Content = JsonContent.Create(new
                {
                    workspacePath = "F:\\dotcraft",
                    kind = "turnCompleted",
                    title = "Done",
                    body = "Finished",
                    threadId = "thread_1",
                    actionUrl = "dotcraft://workspace/open?path=F%3A%5Cdotcraft&threadId=thread_1",
                    openDesktopOnClick = true
                })
            };
            notifyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
            var notify = await http.SendAsync(notifyRequest, cts.Token);
            notify.EnsureSuccessStatusCode();

            var sawEvent = false;
            JsonDocument? eventDoc = null;
            while (!cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == "event: notification.requested")
                {
                    sawEvent = true;
                    continue;
                }

                if (sawEvent && line?.StartsWith("data:", StringComparison.Ordinal) == true)
                {
                    eventDoc = JsonDocument.Parse(line["data:".Length..].Trim());
                    break;
                }
            }

            Assert.True(sawEvent);
            Assert.NotNull(eventDoc);
            using (eventDoc)
            {
                var data = eventDoc.RootElement.GetProperty("data");
                Assert.Equal("thread_1", data.GetProperty("threadId").GetString());
                Assert.Equal("dotcraft://workspace/open?path=F%3A%5Cdotcraft&threadId=thread_1", data.GetProperty("actionUrl").GetString());
                Assert.True(data.GetProperty("openDesktopOnClick").GetBoolean());
            }
        }

        await ShutdownAsync(http, info, cts.Token);
        await runTask.WaitAsync(cts.Token);
    }

    [Fact]
    public async Task RunAsync_EnsureStartsAndReusesManagedAppServer()
    {
        using var workspace = new WorkspaceFixture();
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths, typeof(HubHost).Assembly.Location);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);

        using var http = new HttpClient();
        using var first = AuthorizedPost(info, $"{info.ApiBaseUrl}/v1/appservers/ensure", new
        {
            workspacePath = workspace.WorkspacePath,
            client = new { name = "test", version = "0" },
            startIfMissing = true
        });
        var firstResponse = await http.SendAsync(first, cts.Token);
        var firstBody = await firstResponse.Content.ReadAsStringAsync(cts.Token);
        Assert.True(firstResponse.IsSuccessStatusCode, firstBody);
        using var firstDoc = JsonDocument.Parse(firstBody);
        var firstRoot = firstDoc.RootElement;
        var firstPid = firstRoot.GetProperty("pid").GetInt32();
        Assert.Equal("running", firstRoot.GetProperty("state").GetString());
        Assert.True(firstRoot.GetProperty("startedByHub").GetBoolean());
        Assert.StartsWith("ws://127.0.0.1:", firstRoot.GetProperty("endpoints").GetProperty("appServerWebSocket").GetString());
        Assert.True(File.Exists(paths.AppServersRegistryPath));

        using var second = AuthorizedPost(info, $"{info.ApiBaseUrl}/v1/appservers/ensure", new
        {
            workspacePath = workspace.WorkspacePath,
            startIfMissing = true
        });
        var secondResponse = await http.SendAsync(second, cts.Token);
        secondResponse.EnsureSuccessStatusCode();
        using var secondDoc = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync(cts.Token));
        Assert.Equal(firstPid, secondDoc.RootElement.GetProperty("pid").GetInt32());

        using var stop = AuthorizedPost(info, $"{info.ApiBaseUrl}/v1/appservers/stop", new { workspacePath = workspace.WorkspacePath });
        var stopResponse = await http.SendAsync(stop, cts.Token);
        stopResponse.EnsureSuccessStatusCode();
        await WaitForFileDeletedAsync(Path.Combine(workspace.BotPath, "appserver.lock"), cts.Token);

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{info.ApiBaseUrl}/v1/appservers");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        var listResponse = await http.SendAsync(listRequest, cts.Token);
        listResponse.EnsureSuccessStatusCode();
        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync(cts.Token));
        var listed = Assert.Single(listDoc.RootElement.EnumerateArray());
        Assert.Equal("stopped", listed.GetProperty("state").GetString());
        Assert.Equal(workspace.WorkspacePath, listed.GetProperty("canonicalWorkspacePath").GetString());

        await ShutdownAsync(http, info, cts.Token);
        await runTask.WaitAsync(cts.Token);
    }

    private static async Task<HubLockInfo> WaitForLockAsync(string lockFilePath, Task runTask, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (runTask.IsCompleted)
            {
                await runTask;
                throw new InvalidOperationException("Hub exited before publishing its lock file.");
            }

            var info = HubLockFile.TryRead(lockFilePath);
            if (info is not null)
                return info;

            await Task.Delay(50, ct);
        }

        throw new OperationCanceledException(ct);
    }

    private static async Task WaitForFileDeletedAsync(string path, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!File.Exists(path))
                return;

            await Task.Delay(50, ct);
        }

        throw new OperationCanceledException(ct);
    }

    private static HttpRequestMessage AuthorizedPost(HubLockInfo info, string url, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        return request;
    }

    private static async Task ShutdownAsync(HttpClient http, HubLockInfo info, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{info.ApiBaseUrl}/v1/shutdown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        var shutdown = await http.SendAsync(request, ct);
        shutdown.EnsureSuccessStatusCode();
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        public string WorkspacePath { get; } = Path.Combine(
            Path.GetTempPath(),
            "DotCraftHubWorkspace_" + Guid.NewGuid().ToString("N"));

        public string BotPath { get; }

        public WorkspaceFixture()
        {
            Directory.CreateDirectory(WorkspacePath);
            BotPath = Path.Combine(WorkspacePath, ".craft");
            Directory.CreateDirectory(BotPath);
            File.WriteAllText(Path.Combine(BotPath, "config.json"), """
                {
                  "ProviderId": "openai",
                  "ProviderPreferences": {
                    "openai": {
                      "Model": "gpt-4o-mini",
                      "Reasoning": {
                        "Enabled": false,
                        "Effort": "Medium",
                        "Output": "Full"
                      },
                      "Speed": "Standard",
                      "ContextWindow": {
                        "Mode": "Default"
                      }
                    }
                  },
                  "Providers": {
                    "openai": {
                      "Protocol": "openai-chat-completions",
                      "ApiKey": "test-key",
                      "EndPoint": "https://api.openai.com/v1"
                    }
                  },
                  "McpServers": {},
                  "LspServers": {}
                }
                """);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(WorkspacePath))
                    Directory.Delete(WorkspacePath, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_userProfile))
                Directory.Delete(_userProfile, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
