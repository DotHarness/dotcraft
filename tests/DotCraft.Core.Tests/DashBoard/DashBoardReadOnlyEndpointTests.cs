using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DotCraft.DashBoard;
using DotCraft.Hosting;
using DotCraft.State;
using DotCraft.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardReadOnlyEndpointTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _craft;

    public DashBoardReadOnlyEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DashBoardReadOnly_" + Guid.NewGuid().ToString("N")[..8]);
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        Directory.CreateDirectory(_craft);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    [Fact]
    public async Task RuntimeEndpoint_ReportsReadOnlyCapabilities()
    {
        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.GetAsync("/dashboard/api/runtime");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("readOnly", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("readOnly").GetBoolean());
        var capabilities = root.GetProperty("capabilities");
        Assert.False(capabilities.GetProperty("settings").GetBoolean());
        Assert.False(capabilities.GetProperty("dreams").GetBoolean());
        Assert.False(capabilities.GetProperty("automations").GetBoolean());
        Assert.False(capabilities.GetProperty("sessionDeletion").GetBoolean());
    }

    [Theory]
    [InlineData("DELETE", "/dashboard/api/sessions/thread_one")]
    [InlineData("DELETE", "/dashboard/api/sessions")]
    [InlineData("GET", "/dashboard/api/config/schema")]
    [InlineData("POST", "/dashboard/api/config/workspace")]
    [InlineData("GET", "/dashboard/api/dreams/status")]
    [InlineData("POST", "/dashboard/api/dreams/run")]
    [InlineData("GET", "/dashboard/api/orchestrators/automations/state")]
    [InlineData("POST", "/dashboard/api/orchestrators/automations/refresh")]
    public async Task ReadOnlyDashboard_DoesNotExposeMutationOrDisabledFeatureRoutes(string method, string path)
    {
        await using var app = await CreateDashboardApp();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"Expected {path} to be unavailable, got {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task ReadOnlyDashboard_WithEmptyCraft_DoesNotCreateStateDb()
    {
        await using var app = await CreateDashboardApp();

        Assert.False(File.Exists(Path.Combine(_craft, "state.db")));
    }

    [Fact]
    public void ReadOnlyStoreLoader_ReadsExistingStateDbTraces()
    {
        var stateRuntime = new StateRuntime(_craft);
        var writer = new TraceStore(Path.Combine(_craft, "tracing"), 5000, true, stateRuntime);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread_smoke",
            Type = TraceEventType.Request,
            Content = "compact smoke request",
            Timestamp = DateTimeOffset.UtcNow
        });

        var stores = DashBoardReadOnlyStoreLoader.Load(_craft);

        Assert.True(stores.UsesStateDb);
        Assert.Contains(stores.TraceStore.GetSessions(), session => session.SessionKey == "thread_smoke");
        Assert.Contains(
            stores.TraceStore.GetEvents("thread_smoke"),
            evt => evt.Type == TraceEventType.Request && evt.Content == "compact smoke request");
    }

    private async Task<WebApplication> CreateDashboardApp()
    {
        var stores = DashBoardReadOnlyStoreLoader.Load(_craft);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapDashBoard(
            stores.TraceStore,
            new DotCraftPaths { WorkspacePath = _workspace, CraftPath = _craft },
            stores.TokenUsageStore,
            runtimeOptions: DashBoardRuntimeOptions.ReadOnlyViewer());
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        return app;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
