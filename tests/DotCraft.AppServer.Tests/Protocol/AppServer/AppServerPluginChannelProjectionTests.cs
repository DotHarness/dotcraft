using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task PluginList_ProjectsChannelCatalogMetadataWithoutReadingWorkspaceCredentials()
    {
        var root = Path.Combine(_bundledPluginSourceRoot, "channel-example");
        var plugin = Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        var channel = Directory.CreateDirectory(Path.Combine(root, "channel"));
        File.WriteAllText(Path.Combine(plugin.FullName, "plugin.json"), """
            {
              "schemaVersion": 1,
              "id": "channel-example",
              "displayName": "Example",
              "capabilities": ["channel"],
              "channel": { "manifest": "./channel/manifest.json", "entry": "./channel/entry.mjs", "minHostVersion": "99.0.0" }
            }
            """);
        File.WriteAllText(Path.Combine(channel.FullName, "entry.mjs"), "throw new Error('Not run while listing');");
        File.WriteAllText(Path.Combine(channel.FullName, "manifest.json"), """
            {
              "moduleId": "example-standard",
              "channelName": "example",
              "displayName": "Example",
              "localizedDisplayName": { "zh-Hans": "示例" },
              "packageName": "@example/channel",
              "configFileName": "example.json",
              "variant": "standard",
              "channelContractVersion": "1.0.0",
              "supportedChannelProtocolVersions": ["0.2"],
              "supportedTransports": ["websocket"],
              "requiresInteractiveSetup": false,
              "capabilitySummary": {
                "hasChannelTools": false,
                "requiresInteractiveSetup": false,
                "capabilitySetMayVaryByEnvironment": false
              },
              "launcher": { "bin": "example", "supportsWorkspaceFlag": true, "supportsConfigOverrideFlag": true },
              "interface": { "setupSteps": ["Connect the example bot."], "environment": { "token": "not-metadata" } },
              "configDescriptors": [
                {
                  "key": "example.token",
                  "displayLabel": "Token",
                  "dataKind": "secret",
                  "required": true,
                  "masked": true,
                  "interactiveSetupOnly": false,
                  "defaultValue": "do-not-project"
                },
                {
                  "key": "dotcraft.token",
                  "displayLabel": "Host token",
                  "dataKind": "secret",
                  "required": false,
                  "masked": true,
                  "interactiveSetupOnly": false
                }
              ]
            }
            """);
        File.WriteAllText(Path.Combine(_workspaceCraftPath, "example.json"), """{"example":{"token":"local-workspace-secret"}}""");
        using var harness = CreateHarness();
        using (await harness.InitializeAsync()) { }

        var list = await ReadPluginListResultAsync(harness);
        var pluginInfo = Assert.Single(list.GetProperty("plugins").EnumerateArray(), value =>
            value.GetProperty("id").GetString() == "channel-example");
        Assert.False(pluginInfo.GetProperty("installed").GetBoolean());
        var channelInfo = pluginInfo.GetProperty("channel");
        Assert.Equal("example-standard", channelInfo.GetProperty("moduleId").GetString());
        Assert.Equal("示例", channelInfo.GetProperty("localizedDisplayName").GetProperty("zh-Hans").GetString());
        Assert.Equal("ChannelPluginHostVersionUnsatisfied", channelInfo.GetProperty("compatibilityError").GetString());
        Assert.Equal("Connect the example bot.", channelInfo.GetProperty("interface").GetProperty("setupSteps")[0].GetString());
        Assert.False(channelInfo.TryGetProperty("entry", out _));
        Assert.False(channelInfo.TryGetProperty("launcher", out _));
        Assert.False(channelInfo.TryGetProperty("configFileName", out _));
        Assert.False(channelInfo.GetProperty("interface").TryGetProperty("environment", out _));
        var configField = Assert.Single(channelInfo.GetProperty("configDescriptors").EnumerateArray());
        Assert.Equal("example.token", configField.GetProperty("key").GetString());
        Assert.False(configField.TryGetProperty("defaultValue", out _));
        Assert.DoesNotContain("local-workspace-secret", pluginInfo.GetRawText());
    }
}
