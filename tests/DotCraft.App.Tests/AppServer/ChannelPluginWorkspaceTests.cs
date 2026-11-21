using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.AppServer;

public sealed class ChannelPluginWorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-channel-workspace-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void FreshInstallAndReinstallDoNotInheritPreviousWorkspaceGrant()
    {
        var (service, paths, source) = SetUp();
        var entry = new ChannelEntry
        {
            Name = "fixture", PluginId = "channel-fixture", Enabled = true
        };
        var pluginsRoot = paths.UserData.ResolveOrNull("plugins")!;
        var installationPath = Path.Combine(pluginsRoot, "channel-fixture");
        Assert.Null(entry.PluginActivationId);
        Assert.Throws<AppServerException>(() => service.Authorize(entry));

        File.WriteAllText(paths.Data.Resolve("fixture.json"), """{"adapter":{"token":"configured"}}""");
        service.Authorize(entry);
        var originalGrant = entry.PluginActivationId;
        Assert.Equal(ChannelPluginInstallation.ReadId(installationPath), originalGrant);
        Assert.NotNull(originalGrant);

        Directory.Delete(installationPath, recursive: true);
        Assert.Equal("ChannelPluginNotInstalled", service.GetAdmissionFailure(null, entry));
        Assert.Equal("channel-fixture", new LocalPluginInstaller(pluginsRoot).Install(source).PluginId);
        Assert.NotEqual(originalGrant, ChannelPluginInstallation.ReadId(installationPath));
        Assert.Contains("configured", File.ReadAllText(paths.Data.Resolve("fixture.json")));
    }

    [Fact]
    public void ManagedUpdatePreservesInstallationIdentityAndRejectsIncompatibleCandidate()
    {
        var paths = CreatePaths();
        var source = CreatePlugin(Path.Combine(_root, "catalog"));
        var pluginsRoot = paths.UserData.ResolveOrNull("plugins")!;
        var deployer = new BuiltInPluginDeployer(pluginsRoot, [source], new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true });
        var key = PluginIds.Key("channel-fixture", marketplaceName: null);
        Assert.Empty(deployer.DeployPlugin(key));
        var installed = Path.Combine(pluginsRoot, "channel-fixture");
        var original = ChannelPluginInstallation.ReadId(installed);
        Assert.NotNull(original);

        File.WriteAllText(Path.Combine(source, "launch.mjs"), "export const updated = true;");
        Assert.True(deployer.RefreshPlugin(key, []));
        Assert.Equal(original, ChannelPluginInstallation.ReadId(installed));

        var manifest = Path.Combine(source, ".craft-plugin", "plugin.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("0.0.1", "999.0.0", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(source, "launch.mjs"), "export const incompatible = true;");
        Assert.False(deployer.RefreshPlugin(key, []));
        Assert.Equal("export const updated = true;", File.ReadAllText(Path.Combine(installed, "launch.mjs")));
    }

    private (ChannelPluginWorkspace Service, DotCraftPaths Paths, string Source) SetUp()
    {
        var paths = CreatePaths();
        var source = CreatePlugin(Path.Combine(_root, "candidate"));
        var installed = new LocalPluginInstaller(paths.UserData.ResolveOrNull("plugins")!).Install(source);
        Assert.Equal("channel-fixture", installed.PluginId);
        return (new ChannelPluginWorkspace(paths, new AppConfig(), "0.14.0"), paths, source);
    }

    private DotCraftPaths CreatePaths()
    {
        var workspace = Path.Combine(_root, "workspace");
        var craft = Path.Combine(workspace, ".craft");
        Directory.CreateDirectory(craft);
        return DotCraftPaths.Create(workspace, craft, Path.Combine(_root, "user"));
    }

    internal static string CreatePlugin(string root)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        File.WriteAllText(Path.Combine(path.FullName, "plugin.json"), """
            {
              "schemaVersion": 1, "id": "channel-fixture", "version": "1.0.0", "displayName": "Test Channel",
              "capabilities": ["channel"], "channel": { "manifest": "./manifest.json", "entry": "./launch.mjs", "minHostVersion": "0.0.1" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "manifest.json"), """
            {
              "moduleId": "fixture-standard", "channelName": "fixture", "displayName": "Fixture",
              "packageName": "fixture-channel", "variant": "standard", "configFileName": "fixture.json",
              "supportedTransports": ["websocket"], "channelContractVersion": "1.0.0", "supportedChannelProtocolVersions": ["0.2"],
              "requiresInteractiveSetup": false, "capabilitySummary": {
                "requiresInteractiveSetup": false, "hasChannelTools": false, "capabilitySetMayVaryByEnvironment": false
              },
              "launcher": { "supportsWorkspaceFlag": true },
              "configDescriptors": [
                { "key": "adapter.token", "displayLabel": "Token", "description": "Platform credential", "dataKind": "secret", "required": true, "masked": true, "interactiveSetupOnly": false },
                { "key": "adapter.enabled", "displayLabel": "Enable", "description": "Enable the platform", "dataKind": "boolean", "required": false, "masked": false, "interactiveSetupOnly": false }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(root, "launch.mjs"), "export const launch = true;");
        return root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
