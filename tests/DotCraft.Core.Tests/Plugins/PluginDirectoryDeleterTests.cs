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

            var workspaceTempPath = Path.Combine(craftRoot, "tmp");
            PluginDirectoryDeleter.Delete(pluginRoot, workspaceTempPath);

            Assert.False(Directory.Exists(pluginRoot));
            Assert.True(Directory.Exists(workspaceTempPath));
            Assert.Empty(Directory.EnumerateFileSystemEntries(workspaceTempPath));
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }
}
