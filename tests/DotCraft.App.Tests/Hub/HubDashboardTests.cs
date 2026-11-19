using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.AppServer;
using DotCraft.Common;
using DotCraft.Hub;
using DotCraft.Sessions.Wire;
using Xunit;

namespace DotCraft.Tests.Hub;

public sealed class HubDashboardTests : IDisposable
{
    private readonly string _profile = Path.Combine(Path.GetTempPath(), $"DotCraftHubDashboard_{Guid.NewGuid():N}");

    [Fact]
    public async Task SharedDashboard_RoutesWorkspaceReadsWritesAndLoginWithoutStartingStoppedProjects()
    {
        using var configuredDashboardPort = new TcpListener(IPAddress.Loopback, 0);
        configuredDashboardPort.Start();
        var port = ((IPEndPoint)configuredDashboardPort.LocalEndpoint).Port;
        var alpha = CreateWorkspace("alpha", "alpha-pass", port);
        var beta = CreateWorkspace("beta", "beta-pass", port);
        var stopped = CreateWorkspace("stopped", "stopped-pass", port);
        var paths = HubPaths.Resolve(_profile);
        new ProjectRegistry(paths).Open(stopped);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        await using var host = new HubHost(new HubConfig(), paths, typeof(HubHost).Assembly.Location);
        var running = host.RunAsync(ct.Token);
        var hub = await HubTestStartup.WaitForReadyAsync(paths.LockFilePath, running, ct.Token);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(hub.ApiBaseUrl)
        };

        var (alphaPage, alphaWire) = await EnsureAsync(alpha);
        var (betaPage, _) = await EnsureAsync(beta);
        Assert.Equal(hub.ApiBaseUrl, alphaPage.GetLeftPart(UriPartial.Authority));
        Assert.Equal(hub.ApiBaseUrl, betaPage.GetLeftPart(UriPartial.Authority));
        Assert.NotEqual(alphaPage, betaPage);

        await using (var wire = await WebSocketClientConnection.ConnectAsync(alphaWire, ct: ct.Token))
        {
            using var init = await wire.Wire.InitializeAsync("dashboard-test", "1", ct: ct.Token);
            Assert.Equal(alphaPage.AbsoluteUri,
                init.RootElement.GetProperty("result").GetProperty("dashboardUrl").GetString());
        }

        using (var privateApi = await http.GetAsync(new UriBuilder(alphaWire)
        {
            Scheme = "http", Path = "/dashboard/api/runtime", Query = ""
        }.Uri, ct.Token))
            Assert.Equal(HttpStatusCode.Unauthorized, privateApi.StatusCode);

        var list = Assert.IsType<JsonArray>((await GetAsync(new Uri(http.BaseAddress, "/dashboard/api/workspaces")))["workspaces"]);
        var stoppedWorkspace = Assert.Single(list, item => item?["name"]?.GetValue<string>() == "stopped")!;
        Assert.Equal("stopped", stoppedWorkspace["state"]!.GetValue<string>());
        var stoppedUrl = $"/dashboard/workspaces/{stoppedWorkspace["id"]!.GetValue<string>()}/api/runtime";
        using (var unavailable = await http.GetAsync(stoppedUrl, ct.Token))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.False(File.Exists(Path.Combine(stopped, ".craft", "state.db")));

        using (var wrongHost = new HttpRequestMessage(HttpMethod.Get, new Uri(alphaPage, "api/runtime")))
        {
            wrongHost.Headers.Host = $"example.test:{alphaPage.Port}";
            using var rejected = await http.SendAsync(wrongHost, ct.Token);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }

        using (var unauthenticated = await http.GetAsync(new Uri(alphaPage, "api/runtime"), ct.Token))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using (var login = await http.PostAsJsonAsync(new Uri(alphaPage, "login"),
            new { username = "operator", password = "alpha-pass" }, ct.Token))
        {
            login.EnsureSuccessStatusCode();
            Assert.Contains($"path={alphaPage.AbsolutePath.TrimEnd('/')}",
                Assert.Single(login.Headers.GetValues("Set-Cookie")), StringComparison.OrdinalIgnoreCase);
        }

        using (var unauthenticated = await http.GetAsync(new Uri(betaPage, "api/runtime"), ct.Token))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        using (var management = await http.GetAsync("/v1/appservers", ct.Token))
            Assert.Equal(HttpStatusCode.Unauthorized, management.StatusCode);
        using (var login = await http.PostAsJsonAsync(new Uri(betaPage, "login"),
            new { username = "operator", password = "beta-pass" }, ct.Token))
            login.EnsureSuccessStatusCode();

        Assert.Equal(alpha, (await GetAsync(new Uri(alphaPage, "api/runtime")))["workspacePath"]!.GetValue<string>());
        Assert.Equal(beta, (await GetAsync(new Uri(betaPage, "api/runtime")))["workspacePath"]!.GetValue<string>());

        var betaConfigPath = Path.Combine(beta, ".craft", "config.json");
        var betaConfigBefore = File.ReadAllText(betaConfigPath);
        var edit = Assert.IsType<JsonObject>((await GetAsync(new Uri(alphaPage, "api/config/edit")))["workspace"]);
        edit["Tools"] = JsonNode.Parse("""{ "Lsp": { "Enabled": true } }""");
        using (var forged = new HttpRequestMessage(HttpMethod.Post, new Uri(alphaPage, "api/config/workspace"))
        {
            Content = JsonContent.Create(edit)
        })
        {
            forged.Headers.Add("Origin", "http://example.test");
            using var rejected = await http.SendAsync(forged, ct.Token);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        }
        using (var saved = await http.PostAsJsonAsync(new Uri(alphaPage, "api/config/workspace"), edit, ct.Token))
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync(ct.Token));

        var onDiskAlpha = JsonNode.Parse(File.ReadAllText(Path.Combine(alpha, ".craft", "config.json")))!;
        Assert.True(onDiskAlpha["Tools"]!["Lsp"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal(betaConfigBefore, File.ReadAllText(betaConfigPath));

        using (var stream = await http.GetAsync(new Uri(alphaPage, "api/events/stream"),
            HttpCompletionOption.ResponseHeadersRead, ct.Token))
        {
            stream.EnsureSuccessStatusCode();
            using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(ct.Token));
            Assert.StartsWith(":", await reader.ReadLineAsync(ct.Token));
        }

        using (var logout = await http.PostAsync(new Uri(alphaPage, "logout"), null, ct.Token))
            logout.EnsureSuccessStatusCode();
        using (var unauthenticated = await http.GetAsync(new Uri(alphaPage, "api/runtime"), ct.Token))
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);

        await SendManagementAsync("/v1/appservers/stop", new { workspacePath = alpha });
        using (var unavailable = await http.GetAsync(new Uri(alphaPage, "api/runtime"), ct.Token))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal(beta, (await GetAsync(new Uri(betaPage, "api/runtime")))["workspacePath"]!.GetValue<string>());

        await SendManagementAsync("/v1/shutdown", new { });
        await running.WaitAsync(ct.Token);

        async Task<(Uri Dashboard, Uri Wire)> EnsureAsync(string workspacePath)
        {
            var result = await SendManagementAsync("/v1/appservers/ensure", new { workspacePath, startIfMissing = true });
            Assert.Equal("running", result["state"]!.GetValue<string>());
            var endpoints = result["endpoints"]!;
            return (new Uri(endpoints["dashboard"]!.GetValue<string>()),
                new Uri(endpoints["appServerWebSocket"]!.GetValue<string>()));
        }

        async Task<JsonObject> SendManagementAsync(string path, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hub.Token);
            using var response = await http.SendAsync(request, ct.Token);
            var content = await response.Content.ReadAsStringAsync(ct.Token);
            Assert.True(response.IsSuccessStatusCode, content);
            return Assert.IsType<JsonObject>(JsonNode.Parse(content));
        }

        async Task<JsonObject> GetAsync(Uri uri)
        {
            using var response = await http.GetAsync(uri, ct.Token);
            var content = await response.Content.ReadAsStringAsync(ct.Token);
            Assert.True(response.IsSuccessStatusCode, content);
            return Assert.IsType<JsonObject>(JsonNode.Parse(content));
        }
    }

    private string CreateWorkspace(string name, string password, int dashboardPort)
    {
        var workspace = Path.Combine(_profile, name);
        var data = Path.Combine(workspace, ".craft");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "config.json"), JsonSerializer.Serialize(new
        {
            ProviderId = "test",
            ProviderPreferences = new { test = new { Model = "test-model" } },
            Providers = new { test = new { Protocol = "openai-chat-completions", ApiKey = "test-key", EndPoint = "https://example.test/v1" } },
            DashBoard = new { Enabled = true, Port = dashboardPort, Username = "operator", Password = password },
            Tracing = new { Enabled = true }
        }));
        return workspace;
    }

    public void Dispose() => Directory.Delete(_profile, recursive: true);
}
