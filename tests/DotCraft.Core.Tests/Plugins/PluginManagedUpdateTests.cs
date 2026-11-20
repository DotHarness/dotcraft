using System.IO.Compression;
using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginManagedUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin_update_{Guid.NewGuid():N}");
    private readonly AppConfig.PluginsConfig _plugins = new() { DisableDefaultPluginRegistry = true };
    private string Sources => Path.Combine(_root, "sources");
    private string Installed => Path.Combine(_root, "installed");

    [Fact]
    public void Discovery_DoesNotWriteManagedInstallationWhenSourceChanges()
    {
        WritePlugin("demo", "old");
        new BuiltInPluginDeployer(Installed, [Sources], _plugins).DeployPlugin("demo@dotcraft-bundled");
        WritePlugin("demo", "new");
        using var occupied = File.Open(Path.Combine(Installed, "demo", BuiltInPluginDeployer.MarkerFile),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var discovery = new PluginDiscoveryService(Installed, [Sources])
            .DiscoverAll(new AppConfig { Plugins = _plugins }, _root, Path.Combine(_root, "workspace", ".craft"));

        Assert.DoesNotContain(discovery.Diagnostics, diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        Assert.Equal("old", Assert.Single(discovery.Plugins).Manifest.DisplayName);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
    }

    [Fact]
    public void RefreshPlugin_WhenRemovedBeforeCommit_DoesNotReinstall()
    {
        WritePlugin("demo", "old");
        var deployer = new BuiltInPluginDeployer(Installed, [Sources], _plugins);
        deployer.DeployPlugin("demo@dotcraft-bundled");

        WritePlugin("demo", "new");
        var diagnostics = new List<PluginDiagnostic>();
        var pending = deployer.GetPendingUpdates(diagnostics, requireSourceIdentity: true);
        Assert.Equal(["demo@dotcraft-bundled"], pending);

        PluginDirectoryDeleter.Delete(Path.Combine(Installed, "demo"), Path.Combine(_root, "tmp"));
        Assert.False(Directory.Exists(Path.Combine(Installed, "demo")));

        var updated = deployer.RefreshPlugin("demo@dotcraft-bundled", diagnostics, requireSourceIdentity: true);

        Assert.False(updated);
        Assert.False(Directory.Exists(Path.Combine(Installed, "demo")));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
    }

    [Fact]
    public void RefreshPlugin_WithRequireSourceIdentity_SkipsDifferentOrUnboundMarketplaceSource()
    {
        var marketplaceA = Path.Combine(_root, "marketplace-a");
        var marketplaceB = Path.Combine(_root, "marketplace-b");
        WriteLocalMarketplace(marketplaceA, "team-mp", "shared", "from-a");
        WriteLocalMarketplace(marketplaceB, "team-mp", "shared", "from-b");

        var configA = CreateLocalMarketplaceConfig("team-mp", marketplaceA);
        new BuiltInPluginDeployer(Installed, [], configA).DeployPlugin("shared@team-mp");
        Assert.Equal("from-a", File.ReadAllText(Path.Combine(Installed, "shared", "content.txt")));

        var configB = CreateLocalMarketplaceConfig("team-mp", marketplaceB);
        var deployerB = new BuiltInPluginDeployer(Installed, [], configB);
        var diagnostics = new List<PluginDiagnostic>();
        Assert.Empty(deployerB.GetPendingUpdates(diagnostics, requireSourceIdentity: true));
        Assert.False(deployerB.RefreshPlugin("shared@team-mp", diagnostics, requireSourceIdentity: true));
        Assert.Equal("from-a", File.ReadAllText(Path.Combine(Installed, "shared", "content.txt")));

        var markerPath = Path.Combine(Installed, "shared", BuiltInPluginDeployer.MarkerFile);
        var legacyLines = File.ReadAllLines(markerPath)
            .Where(line => !line.StartsWith("source=", StringComparison.Ordinal))
            .ToArray();
        File.WriteAllText(markerPath, string.Join('\n', legacyLines));

        WriteLocalMarketplace(marketplaceA, "team-mp", "shared", "from-a-v2");
        var deployerA = new BuiltInPluginDeployer(Installed, [], configA);
        Assert.Empty(deployerA.GetPendingUpdates(diagnostics, requireSourceIdentity: true));
        Assert.False(deployerA.RefreshPlugin("shared@team-mp", diagnostics, requireSourceIdentity: true));
        Assert.Equal("from-a", File.ReadAllText(Path.Combine(Installed, "shared", "content.txt")));
    }

    [Fact]
    public void DeployPlugin_HashesRegistrySourceIdentityWithoutPersistingQueryToken()
    {
        const string signedUrl = "https://registry.test/archive.zip?sig=secret-token-123";
        const string marketplacePath = ".craft/plugins/marketplace.json";
        var craftHome = Path.Combine(_root, "craft-home");
        var pluginsConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = "signed-mp",
                    SourceType = "archive",
                    Url = signedUrl,
                    MarketplacePath = marketplacePath
                }
            ]
        };

        var cache = new PluginRegistryArchiveCache(craftHome);
        cache.Activate(signedUrl, marketplacePath, CreateRegistryArchive("signed-mp", "signed-demo", "v1"));
        var deployer = new BuiltInPluginDeployer(Installed, [], pluginsConfig, craftHome);
        deployer.DeployPlugin("signed-demo@signed-mp");

        var markerText = File.ReadAllText(Path.Combine(Installed, "signed-demo", BuiltInPluginDeployer.MarkerFile));
        Assert.Contains("source=archive|", markerText, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token-123", markerText, StringComparison.Ordinal);
        Assert.DoesNotContain("registry.test", markerText, StringComparison.Ordinal);

        cache.Activate(signedUrl, marketplacePath, CreateRegistryArchive("signed-mp", "signed-demo", "v2"));
        var diagnostics = new List<PluginDiagnostic>();
        Assert.True(deployer.RefreshPlugin("signed-demo@signed-mp", diagnostics, requireSourceIdentity: true));
        Assert.Equal("v2", File.ReadAllText(Path.Combine(Installed, "signed-demo", "content.txt")));
    }

    [Fact]
    public void RefreshPlugin_WhenSourceBecomesDotNetPluginBeforeCommit_SkipsUpdate()
    {
        WritePlugin("demo", "old");
        var deployer = new BuiltInPluginDeployer(Installed, [Sources], _plugins);
        deployer.DeployPlugin("demo@dotcraft-bundled");

        WritePlugin("demo", "new");
        var diagnostics = new List<PluginDiagnostic>();
        Assert.Equal(["demo@dotcraft-bundled"], deployer.GetPendingUpdates(diagnostics, requireSourceIdentity: true));

        Directory.CreateDirectory(Path.Combine(Sources, "demo", "dotnet"));
        File.WriteAllBytes(Path.Combine(Sources, "demo", "dotnet", "demo.dll"), []);
        File.WriteAllText(Path.Combine(Sources, "demo", "dotnet", "demo.deps.json"), "{}");
        File.WriteAllText(Path.Combine(Sources, "demo", ".craft-plugin", "plugin.json"), """
            {
              "schemaVersion": 1,
              "id": "demo",
              "version": "2.0.0",
              "displayName": "new",
              "description": "Converted to .NET plugin.",
              "dotnet": {
                "minHostVersion": "0.1.0",
                "entryAssembly": "./dotnet/demo.dll",
                "entryType": "Demo.Ext"
              },
              "interface": { "displayName": "new" }
            }
            """);
        Assert.NotNull(PluginManifestParser.Load(Path.Combine(Sources, "demo")).Manifest?.Dotnet);

        Assert.False(deployer.RefreshPlugin("demo@dotcraft-bundled", diagnostics, requireSourceIdentity: true));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
    }

    [Fact]
    public async Task RefreshPlugin_WhenCanceledWhileWaitingForPluginsRootLock_ThrowsWithoutModifyingInstalledPlugin()
    {
        WritePlugin("demo", "old");
        var deployer = new BuiltInPluginDeployer(Installed, [Sources], _plugins);
        deployer.DeployPlugin("demo@dotcraft-bundled");
        WritePlugin("demo", "new");

        using var lockHeld = new ManualResetEventSlim();
        using var releaseLock = new ManualResetEventSlim();
        var holder = Task.Run(() =>
            BuiltInPluginDeployer.WithPluginsRootLock(Installed, () =>
            {
                lockHeld.Set();
                releaseLock.Wait(TimeSpan.FromSeconds(10));
                return true;
            }));

        Assert.True(lockHeld.Wait(TimeSpan.FromSeconds(5)));
        using var cts = new CancellationTokenSource();
        var refreshTask = Task.Run(() =>
            deployer.RefreshPlugin("demo@dotcraft-bundled", [], requireSourceIdentity: true, cts.Token));
        try
        {
            await Task.Delay(50);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => refreshTask.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            releaseLock.Set();
            await holder;
        }

        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
    }

    private static byte[] CreateRegistryArchive(string marketplaceName, string pluginId, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, "repo/.craft/plugins/marketplace.json", $$"""
                {
                  "name": "{{marketplaceName}}",
                  "plugins": [
                    {
                      "name": "{{pluginId}}",
                      "source": { "source": "local", "path": "./plugins/{{pluginId}}" },
                      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                      "category": "Testing"
                    }
                  ]
                }
                """);
            WriteZipEntry(archive, $"repo/plugins/{pluginId}/.craft-plugin/plugin.json", $$"""
                {
                  "schemaVersion": 1,
                  "id": "{{pluginId}}",
                  "version": "1.0.0",
                  "displayName": "{{content}}",
                  "description": "Registry plugin fixture.",
                  "interface": { "displayName": "{{content}}" }
                }
                """);
            WriteZipEntry(archive, $"repo/plugins/{pluginId}/content.txt", content);
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private static AppConfig.PluginsConfig CreateLocalMarketplaceConfig(string name, string directory) =>
        new()
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = name,
                    SourceType = "local",
                    Url = directory
                }
            ]
        };

    private static void WriteLocalMarketplace(string root, string marketplaceName, string pluginId, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, ".craft", "plugins"));
        File.WriteAllText(Path.Combine(root, ".craft", "plugins", "marketplace.json"), $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginId}}",
                  "source": { "source": "local", "path": "./plugins/{{pluginId}}" },
                  "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                  "category": "Testing"
                }
              ]
            }
            """);
        var pluginRoot = Path.Combine(root, "plugins", pluginId);
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{pluginId}}",
              "version": "1.0.0",
              "displayName": "{{content}}",
              "description": "Marketplace plugin fixture.",
              "interface": { "displayName": "{{content}}" }
            }
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "content.txt"), content);
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
