using DotCraft.Plugins;

namespace DotCraft.Core.Tests.Plugins;

public sealed class LocalPluginInstallerTests
{
    [Fact]
    public void Install_CopiesValidPluginAsRemovableWorkspacePlugin()
    {
        var root = NewTempDir();
        var source = Path.Combine(root, "my-plugin");
        WritePlugin(source, "my-plugin");
        var workspacePlugins = Path.Combine(root, ".craft", "plugins");

        var result = new LocalPluginInstaller(workspacePlugins).Install(source);

        Assert.Equal("my-plugin", result.PluginId);
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);

        var installed = Path.Combine(workspacePlugins, "my-plugin");
        Assert.True(File.Exists(Path.Combine(installed, ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(installed, "skills", "demo-skill", "SKILL.md")));
        // No built-in marker is written, so discovery treats it as a removable workspace plugin.
        Assert.False(File.Exists(Path.Combine(installed, BuiltInPluginDeployer.MarkerFile)));
        Assert.False(BuiltInPluginDeployer.IsManagedBuiltInPluginRoot(installed));
    }

    [Fact]
    public void Install_DropsAnyStrayBuiltInMarkerFromSource()
    {
        var root = NewTempDir();
        var source = Path.Combine(root, "my-plugin");
        WritePlugin(source, "my-plugin");
        File.WriteAllText(Path.Combine(source, BuiltInPluginDeployer.MarkerFile), "filesystem;sha256:deadbeef");
        var workspacePlugins = Path.Combine(root, ".craft", "plugins");

        var result = new LocalPluginInstaller(workspacePlugins).Install(source);

        Assert.Equal("my-plugin", result.PluginId);
        Assert.False(File.Exists(Path.Combine(workspacePlugins, "my-plugin", BuiltInPluginDeployer.MarkerFile)));
    }

    [Fact]
    public void Install_RejectsFolderWithoutManifest()
    {
        var root = NewTempDir();
        var source = Path.Combine(root, "not-a-plugin");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "readme.txt"), "hi");
        var workspacePlugins = Path.Combine(root, ".craft", "plugins");

        var result = new LocalPluginInstaller(workspacePlugins).Install(source);

        Assert.Null(result.PluginId);
        Assert.Contains(result.Diagnostics, d =>
            d.Severity == PluginDiagnosticSeverity.Error && d.Code == "LocalPluginManifestMissing");
        Assert.False(Directory.Exists(workspacePlugins));
    }

    [Fact]
    public void Install_RejectsMissingDirectory()
    {
        var root = NewTempDir();

        var result = new LocalPluginInstaller(Path.Combine(root, ".craft", "plugins"))
            .Install(Path.Combine(root, "does-not-exist"));

        Assert.Null(result.PluginId);
        Assert.Contains(result.Diagnostics, d => d.Code == "LocalPluginPathMissing");
    }

    [Fact]
    public void Install_RejectsRelativePathWithoutWritingWorkspacePlugin()
    {
        var root = NewTempDir();
        var workspacePlugins = Path.Combine(root, ".craft", "plugins");
        var relativeSource = "dotcraft-relative-plugin-test-" + Guid.NewGuid().ToString("N");
        var source = Path.Combine(Directory.GetCurrentDirectory(), relativeSource);

        try
        {
            WritePlugin(source, "my-plugin");

            var result = new LocalPluginInstaller(workspacePlugins).Install(relativeSource);

            Assert.Null(result.PluginId);
            Assert.Contains(result.Diagnostics, d =>
                d.Severity == PluginDiagnosticSeverity.Error && d.Code == "LocalPluginPathNotAbsolute");
            Assert.False(Directory.Exists(workspacePlugins));
        }
        finally
        {
            if (Directory.Exists(source))
                Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public void Install_RejectsBlankPath()
    {
        var root = NewTempDir();

        var result = new LocalPluginInstaller(Path.Combine(root, ".craft", "plugins")).Install("   ");

        Assert.Null(result.PluginId);
        Assert.Contains(result.Diagnostics, d => d.Code == "LocalPluginPathRequired");
    }

    [Fact]
    public void Install_RejectsWhenAlreadyInstalledWithoutClobbering()
    {
        var root = NewTempDir();
        var source = Path.Combine(root, "my-plugin");
        WritePlugin(source, "my-plugin");
        var workspacePlugins = Path.Combine(root, ".craft", "plugins");
        var installer = new LocalPluginInstaller(workspacePlugins);

        Assert.Equal("my-plugin", installer.Install(source).PluginId);

        var sentinel = Path.Combine(workspacePlugins, "my-plugin", "sentinel.txt");
        File.WriteAllText(sentinel, "keep");

        var second = installer.Install(source);

        Assert.Null(second.PluginId);
        Assert.Contains(second.Diagnostics, d => d.Code == "LocalPluginAlreadyInstalled");
        Assert.True(File.Exists(sentinel));
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dotcraft-localplugin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePlugin(string pluginRoot, string id)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }
}
