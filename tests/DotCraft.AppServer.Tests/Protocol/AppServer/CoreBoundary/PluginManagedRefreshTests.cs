using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Skills;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class PluginManagedRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin_refresh_{Guid.NewGuid():N}");
    private readonly AppConfig.PluginsConfig _plugins = new() { DisableDefaultPluginRegistry = true };
    private string Sources => Path.Combine(_root, "sources");
    private string UserData => Path.Combine(_root, "user");
    private string Installed => Path.Combine(UserData, "plugins");

    [Fact]
    public async Task MarketplaceRefresh_UpdatesManagedInstallationsButPluginListDoesNot()
    {
        WritePlugin("demo", "old");
        new BuiltInPluginDeployer(Installed, [Sources], _plugins).Deploy();
        WritePlugin("demo", "new");
        using var harness = CreateHarness();
        using (await harness.InitializeAsync()) { }

        using (var list = await ListPluginsAsync(harness))
        {
            AppServerTestHarness.AssertIsSuccessResponse(list);
            Assert.Equal("old", Assert.Single(list.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray())
                .GetProperty("displayName").GetString());
        }

        using (var refreshed = await RefreshAsync(harness))
            Assert.Empty(refreshed.RootElement.GetProperty("result").GetProperty("errors").EnumerateArray());

        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
        using var updatedList = await ListPluginsAsync(harness);
        AppServerTestHarness.AssertIsSuccessResponse(updatedList);
        Assert.Equal("new", Assert.Single(updatedList.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray())
            .GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task MarketplaceRefresh_FailureKeepsOldPluginAndDoesNotBlockOtherUpdatesOrReads()
    {
        WritePlugin("blocked", "old");
        WritePlugin("working", "old");
        new BuiltInPluginDeployer(Installed, [Sources], _plugins).Deploy();
        WritePlugin("blocked", "new");
        WritePlugin("working", "new");
        using var harness = CreateHarness();
        using (await harness.InitializeAsync()) { }
        using var occupied = File.Open(
            Path.Combine(OperatingSystem.IsWindows() ? Installed : Sources, "blocked", "content.txt"),
            FileMode.Open, FileAccess.Read, OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None);

        using (var refreshed = await RefreshAsync(harness))
        {
            var failure = Assert.Single(refreshed.RootElement.GetProperty("result").GetProperty("errors").EnumerateArray());
            Assert.Equal("BuiltInPluginDeployFailed", failure.GetProperty("code").GetString());
            Assert.Equal("blocked", failure.GetProperty("name").GetString());
        }

        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "blocked", "content.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "working", "content.txt")));
        using var list = await ListPluginsAsync(harness);
        AppServerTestHarness.AssertIsSuccessResponse(list);
        Assert.Equal(2, list.RootElement.GetProperty("result").GetProperty("plugins").GetArrayLength());
    }

    private AppServerTestHarness CreateHarness()
    {
        var craftPath = Path.Combine(_root, "workspace", ".craft");
        Directory.CreateDirectory(craftPath);
        var config = new AppConfig { GlobalConfigPath = Path.Combine(UserData, "config.json"), Plugins = _plugins };
        return new AppServerTestHarness(
            workspaceCraftPath: craftPath,
            skillsLoader: new SkillsLoader(craftPath),
            appConfigMonitor: new AppConfigMonitor(config),
            builtInPluginSourceRoots: [Sources]);
    }

    private static async Task<System.Text.Json.JsonDocument> ListPluginsAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true }));
        return await harness.Transport.ReadNextSentAsync();
    }

    private static async Task<System.Text.Json.JsonDocument> RefreshAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.MarketplaceRefresh, new { }));
        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var notification = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated,
            notification.RootElement.GetProperty("method").GetString());
        return response;
    }

    private void WritePlugin(string id, string content)
    {
        var plugin = Path.Combine(Sources, id);
        Directory.CreateDirectory(Path.Combine(plugin, ".craft-plugin"));
        File.WriteAllText(Path.Combine(plugin, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "1.0.0",
              "displayName": "{{content}}",
              "description": "Managed plugin fixture.",
              "interface": { "displayName": "{{content}}" }
            }
            """);
        File.WriteAllText(Path.Combine(plugin, "content.txt"), content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
