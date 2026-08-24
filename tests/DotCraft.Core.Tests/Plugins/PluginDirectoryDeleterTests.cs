using DotCraft.Plugins;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginDirectoryDeleterTests
{
    [Fact]
    public void Delete_CommitsByMovingRootOutsidePluginDiscovery()
    {
        var craftRoot = Path.Combine(
            Path.GetTempPath(),
            $"dotcraft_plugin_delete_{Guid.NewGuid():N}",
            ".craft");
        var pluginRoot = Path.Combine(craftRoot, "plugins", "acme.review");
        var testRoot = Directory.GetParent(craftRoot)!.FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
            File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), "{}");

            PluginDirectoryDeleter.Delete(pluginRoot);

            Assert.False(Directory.Exists(pluginRoot));
            var trashRoot = Path.Combine(craftRoot, ".plugin-trash");
            Assert.True(Directory.Exists(trashRoot));
            Assert.Empty(Directory.EnumerateDirectories(trashRoot));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }
}
