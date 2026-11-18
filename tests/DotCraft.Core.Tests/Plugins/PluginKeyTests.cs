using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginKeyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin_keys_{Guid.NewGuid():N}");
    private string UserData => Path.Combine(_root, "user");
    private string Installed => Path.Combine(UserData, "plugins");

    [Fact]
    public void Catalog_ListsOnePluginIdOncePerSourceWithoutDiagnostics()
    {
        var bundled = Path.Combine(_root, "bundled");
        WritePlugin(Path.Combine(bundled, "demo"), "bundled");
        var config = Config("alpha", "beta");
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
        {
            Name = "alpha-again",
            SourceType = "local",
            Url = MarketplaceRoot("alpha")
        });

        var result = new BuiltInPluginCatalog([bundled], config.Plugins, UserData).Discover();

        Assert.Equal(["demo@dotcraft-bundled", "demo@alpha", "demo@beta"], result.Plugins.Select(plugin => plugin.Key));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Discovery_KeepsOtherMarketplaceEntriesOfAnInstalledPlugin()
    {
        var config = Config("alpha", "beta");
        Assert.DoesNotContain(
            new BuiltInPluginDeployer(Installed, [], config.Plugins, UserData).DeployPlugin("demo@beta"),
            diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);

        var workspace = Path.Combine(_root, "workspace");
        var discovery = new PluginDiscoveryService(Installed, [], UserData)
            .DiscoverAll(config, workspace, Path.Combine(workspace, ".craft"));

        var installed = Assert.Single(discovery.Plugins);
        Assert.True(installed.Installed);
        Assert.Equal("demo@beta", installed.Key);
        var other = Assert.Single(discovery.AdditionalCatalogEntries);
        Assert.Equal("demo@alpha", other.Key);
        Assert.False(other.Installable);
        Assert.DoesNotContain(discovery.Diagnostics, diagnostic => diagnostic.Severity != PluginDiagnosticSeverity.Info);
    }

    [Fact]
    public void Deployer_InstallsAndUpdatesOnlyFromTheRecordedMarketplace()
    {
        var config = Config("alpha", "beta");
        var deployer = new BuiltInPluginDeployer(Installed, [], config.Plugins, UserData);
        deployer.DeployPlugin("demo@alpha");

        var conflict = deployer.DeployPlugin("demo@beta");
        WritePlugin(Path.Combine(MarketplaceRoot("beta"), "plugins", "demo"), "beta-new");
        var pendingForOtherSource = deployer.GetPendingUpdates([]);
        WritePlugin(Path.Combine(MarketplaceRoot("alpha"), "plugins", "demo"), "alpha-new");
        var pendingForRecordedSource = deployer.GetPendingUpdates([]);

        Assert.Contains(conflict, diagnostic => diagnostic.Code == "PluginInstalledFromOtherSource");
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
        Assert.Empty(pendingForOtherSource);
        Assert.Equal(["demo@alpha"], pendingForRecordedSource);
    }

    [Fact]
    public void Deployer_IgnoresAConfiguredMarketplaceUsingTheBundledName()
    {
        var bundled = Path.Combine(_root, "bundled");
        WritePlugin(Path.Combine(bundled, "demo"), "bundled");
        new BuiltInPluginDeployer(Installed, [bundled], new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true }, UserData)
            .DeployPlugin("demo@dotcraft-bundled");
        var config = Config(PluginIds.BundledMarketplace);
        var diagnostics = new List<PluginDiagnostic>();

        var pending = new BuiltInPluginDeployer(Installed, [], config.Plugins, UserData).GetPendingUpdates(diagnostics);

        Assert.Empty(pending);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "PluginRegistryNameReserved");
        Assert.Equal("bundled", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
    }

    private AppConfig Config(params string[] marketplaces)
    {
        var config = new AppConfig();
        config.Plugins.DisableDefaultPluginRegistry = true;
        foreach (var name in marketplaces)
        {
            WriteMarketplace(name);
            config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
            {
                Name = name,
                SourceType = "local",
                Url = MarketplaceRoot(name)
            });
        }

        return config;
    }

    private string MarketplaceRoot(string name) => Path.Combine(_root, "marketplaces", name);

    private void WriteMarketplace(string name)
    {
        var root = MarketplaceRoot(name);
        Directory.CreateDirectory(Path.Combine(root, ".craft", "plugins"));
        File.WriteAllText(Path.Combine(root, ".craft", "plugins", "marketplace.json"), $$"""
            {
              "name": "{{name}}",
              "plugins": [
                {
                  "name": "demo",
                  "source": { "source": "local", "path": "./plugins/demo" },
                  "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                  "category": "Testing"
                }
              ]
            }
            """);
        WritePlugin(Path.Combine(root, "plugins", "demo"), name);
    }

    private static void WritePlugin(string pluginRoot, string content)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "demo",
              "version": "1.0.0",
              "displayName": "{{content}}",
              "description": "Plugin key fixture.",
              "interface": { "displayName": "{{content}}" }
            }
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "content.txt"), content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
