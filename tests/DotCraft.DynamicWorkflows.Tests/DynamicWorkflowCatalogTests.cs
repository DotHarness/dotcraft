using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.DynamicWorkflows.Tests;

public sealed class DynamicWorkflowCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "workflow-catalog-" + Guid.NewGuid().ToString("N"));
    private string DataPath => Path.Combine(_root, ".craft");
    private string PluginPath => Path.Combine(DataPath, "plugins", "example");

    [Fact]
    public void PluginSummaryUsesTheDiscoveredManifestAndReadsCurrentWorkflowContent()
    {
        var (discovery, catalog, config) = CreateCatalog();
        var plugin = Assert.Single(discovery.DiscoverAll(config, _root, DataPath).Plugins);
        File.Delete(plugin.Manifest.ManifestPath);

        var summary = Assert.Single(catalog.ListForPlugin(plugin));
        Assert.Equal("review", summary.Name);
        Assert.Equal("/example:review", summary.Command);
        Assert.Equal("Review", summary.Description);
        Assert.Equal("When requested", summary.WhenToUse);

        File.WriteAllText(Path.Combine(PluginPath, "workflows", "review.js"),
            "export const meta = { name: 'review', description: 'Updated review' }; return null;");
        Assert.Equal("Updated review", Assert.Single(catalog.ListForPlugin(plugin)).Description);
        Assert.Empty(discovery.DiscoverAll(config, _root, DataPath).Plugins);
    }

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 0)]
    public void PluginSummaryRequiresInstallationButNotEnablement(bool installed, bool enabled, int count)
    {
        var (discovery, catalog, config) = CreateCatalog();
        var plugin = Assert.Single(discovery.DiscoverAll(config, _root, DataPath).Plugins)
            with { Installed = installed, Enabled = enabled };

        Assert.Equal(count, catalog.ListForPlugin(plugin).Count);
    }

    private (PluginDiscoveryService Discovery, DynamicWorkflowCatalog Catalog, AppConfig Config) CreateCatalog()
    {
        Directory.CreateDirectory(Path.Combine(PluginPath, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(PluginPath, "workflows"));
        File.WriteAllText(Path.Combine(PluginPath, ".craft-plugin", "plugin.json"),
            """{ "schemaVersion": 1, "id": "example", "displayName": "Example", "workflows": "./workflows" }""");
        File.WriteAllText(Path.Combine(PluginPath, "workflows", "review.js"),
            "export const meta = { name: 'review', description: 'Review', whenToUse: 'When requested' }; return null;");
        var config = new AppConfig();
        config.Plugins.DisableDefaultPluginRegistry = true;
        var discovery = new PluginDiscoveryService(
            userGlobalPluginsPath: Path.Combine(_root, "user", "plugins"),
            builtInPluginSourceRoots: [],
            craftHome: Path.Combine(_root, "user"));
        var catalog = new DynamicWorkflowCatalog(
            _root, DataPath, userDataPath: null, config, new DynamicWorkflowParser(), discovery);
        return (discovery, catalog, config);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
