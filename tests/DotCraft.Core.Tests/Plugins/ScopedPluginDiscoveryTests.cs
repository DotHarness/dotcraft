using DotCraft.Configuration;
using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class ScopedPluginDiscoveryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("plugin-scope-").FullName;

    [Fact]
    public void ExplicitScopeUsesRootOrderAndDoesNotInheritOtherSources()
    {
        var primary = Path.Combine(_root, "primary");
        var secondary = Path.Combine(_root, "secondary");
        var ambient = Path.Combine(_root, "ambient");
        WritePlugin(primary, "fixture-plugin");
        WritePlugin(primary, "unlisted-plugin");
        WritePlugin(secondary, "fixture-plugin");
        WritePlugin(ambient, "ambient-plugin");

        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(ambient);
        var discovery = new PluginDiscoveryService(userGlobalPluginsPath: ambient);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "fixture-plugin", "ambient-plugin" };
        var result = discovery.DiscoverDirectories([primary, secondary], config, allowed);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("fixture-plugin", plugin.Manifest.Id);
        Assert.Equal(primary, plugin.SourceRoot);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "DuplicatePluginId" && diagnostic.PluginId == "fixture-plugin");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "PluginNotFound" && diagnostic.PluginId == "ambient-plugin");
    }

    private static void WritePlugin(string directory, string id)
    {
        var plugin = Path.Combine(directory, id);
        var manifestDirectory = Directory.CreateDirectory(Path.Combine(plugin, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(plugin, "skills", "example"));
        File.WriteAllText(Path.Combine(plugin, "skills", "example", "SKILL.md"),
            "---\nname: example\ndescription: Example skill\n---\n# Example");
        File.WriteAllText(Path.Combine(manifestDirectory.FullName, "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "1.0.0",
              "displayName": "Fixture",
              "capabilities": ["skill"],
              "skills": "./skills/"
            }
            """);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
