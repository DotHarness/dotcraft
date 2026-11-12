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
        new BuiltInPluginDeployer(Installed, [Sources], _plugins).DeployPlugin("demo");
        WritePlugin("demo", "new");
        using var occupied = File.Open(Path.Combine(Installed, "demo", BuiltInPluginDeployer.MarkerFile),
            FileMode.Open, FileAccess.Read, FileShare.None);

        var discovery = new PluginDiscoveryService(Installed, [Sources])
            .DiscoverAll(new AppConfig { Plugins = _plugins }, _root, Path.Combine(_root, "workspace", ".craft"));

        Assert.DoesNotContain(discovery.Diagnostics, diagnostic => diagnostic.Severity == PluginDiagnosticSeverity.Error);
        Assert.Equal("old", Assert.Single(discovery.Plugins).Manifest.DisplayName);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "demo", "content.txt")));
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
