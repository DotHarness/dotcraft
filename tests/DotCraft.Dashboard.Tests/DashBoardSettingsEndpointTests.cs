using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Persistence;
using DotCraft.Tracing;
using DotCraft.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DotCraft.Tests.DashBoard;

public sealed class DashBoardSettingsEndpointTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DashBoardSettings_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _workspace;
    private readonly string _craft;
    private readonly string _userConfigPath;
    private readonly string _workspaceConfigPath;
    private readonly List<AppConfigChangedEventArgs> _events = [];
    private AppConfigMonitor? _monitor;

    public DashBoardSettingsEndpointTests()
    {
        _workspace = Path.Combine(_root, "workspace");
        _craft = Path.Combine(_workspace, ".craft");
        _userConfigPath = Path.Combine(_root, "home", ".craft", "config.json");
        _workspaceConfigPath = Path.Combine(_craft, "config.json");
        Directory.CreateDirectory(_craft);
        Directory.CreateDirectory(Path.GetDirectoryName(_userConfigPath)!);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public async Task SaveWorkspace_ValidChange_UpdatesRuntimeAndRaisesOneChangeEvent()
    {
        File.WriteAllText(_workspaceConfigPath, """{ "DashBoard": { "Username": "admin", "Password": "secret" } }""");
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var edit = await GetJsonAsync(http, "/dashboard/api/config/edit");
        var workspace = (JsonObject)edit["workspace"]!;
        Assert.Equal("***", workspace["DashBoard"]!["Password"]!.GetValue<string>());
        workspace["Tools"] = new JsonObject { ["Lsp"] = new JsonObject { ["Enabled"] = true } };

        using var response = await http.PostAsJsonAsync("/dashboard/api/config/workspace", workspace);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_monitor!.Current.Tools.Lsp.Enabled);
        var change = Assert.Single(_events);
        Assert.Equal(["Tools.Lsp.Enabled"], change.Regions);
        var saved = JsonNode.Parse(File.ReadAllText(_workspaceConfigPath))!;
        Assert.Equal("secret", saved["DashBoard"]!["Password"]!.GetValue<string>());
        Assert.True(saved["Tools"]!["Lsp"]!["Enabled"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SaveWorkspace_InvalidDocument_IsRejectedAndLeavesFileUnchanged()
    {
        const string original = """{ "Tools": { "Lsp": { "Enabled": false } } }""";
        File.WriteAllText(_workspaceConfigPath, original);
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        using var response = await http.PostAsJsonAsync(
            "/dashboard/api/config/workspace",
            JsonNode.Parse("""{ "Tools": { "Lsp": { "Enabled": "sometimes" } } }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, File.ReadAllText(_workspaceConfigPath));
        Assert.False(_monitor!.Current.Tools.Lsp.Enabled);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task EditView_MergesLayersCaseInsensitively()
    {
        File.WriteAllText(_userConfigPath, """{ "tools": { "lsp": { "enabled": true } } }""");
        File.WriteAllText(_workspaceConfigPath, """{ "Tools": { "Lsp": { "Enabled": false } } }""");
        await using var app = await StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };

        var edit = await GetJsonAsync(http, "/dashboard/api/config/edit");

        Assert.Equal(_userConfigPath, edit["globalPath"]!.GetValue<string>());
        Assert.Equal(_workspaceConfigPath, edit["workspacePath"]!.GetValue<string>());
        var merged = (JsonObject)edit["merged"]!;
        var tools = Assert.Single(merged, property => string.Equals(property.Key, "Tools", StringComparison.OrdinalIgnoreCase));
        var lsp = Assert.Single((JsonObject)tools.Value!, property => string.Equals(property.Key, "Lsp", StringComparison.OrdinalIgnoreCase));
        var enabled = Assert.Single((JsonObject)lsp.Value!, property => string.Equals(property.Key, "Enabled", StringComparison.OrdinalIgnoreCase));
        Assert.False(enabled.Value!.GetValue<bool>());
    }

    private async Task<WebApplication> StartAsync()
    {
        _monitor = new AppConfigMonitor(AppConfig.LoadWithGlobalFallback(_workspaceConfigPath, _userConfigPath));
        _monitor.Changed += (_, args) => _events.Add(args);
        var configuration = new ConfigurationService(
            ConfigSchemaRegistrations.CreateDescriptorRegistry(),
            _monitor,
            _userConfigPath,
            _workspaceConfigPath);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapDashBoard(
            new TraceStore(new WorkspaceStateDatabase(_craft), 5000, synchronousPersist: true),
            new DotCraftPaths(_workspace, _craft, userDataPath: null),
            configuration: configuration);
        app.Urls.Add($"http://127.0.0.1:{GetFreeTcpPort()}");
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonObject> GetJsonAsync(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
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
