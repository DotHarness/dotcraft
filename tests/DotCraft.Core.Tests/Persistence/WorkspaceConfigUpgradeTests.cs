using System.Text.Json.Nodes;
using DotCraft.Persistence.Upgrades;
using Xunit;

namespace DotCraft.Tests.Persistence;

public sealed class WorkspaceConfigUpgradeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-config-upgrade", Guid.NewGuid().ToString("N"));
    private string CraftPath => Path.Combine(_root, "workspace", ".craft");
    private string WorkspaceConfig => Path.Combine(CraftPath, "config.json");
    private string UserConfig => Path.Combine(_root, "user", "config.json");

    public WorkspaceConfigUpgradeTests() => Directory.CreateDirectory(CraftPath);

    [Fact]
    public void MapsPluginSelectionsAndPreservesPlatformConfiguration()
    {
        File.WriteAllText(WorkspaceConfig, """
            {
              "ProviderId": "local",
              "ExternalChannels": {
                "Telegram": { "enabled": true, "transport": "managedWebsocket", "BuiltinModule": "channel-telegram" },
                "weixin": { "enabled": false, "builtinModule": "channel-weixin" },
                "qq": { "enabled": true, "transport": "subprocess", "command": "qq-local" },
                "custom": { "enabled": true, "builtinModule": "channel-custom" },
                "feishu": { "enabled": true, "pluginId": "community-feishu", "transport": "managedWebsocket", "pluginActivationId": "existing-grant" }
              },
              "Channels": { "feishu": { "enabled": true, "pluginId": "community-feishu" } }
            }
            """);
        var authFile = Path.Combine(CraftPath, "telegram.json");
        File.WriteAllText(authFile, """{ "telegram": { "botToken": "keep" } }""");
        File.WriteAllText(Path.Combine(CraftPath, "upgrade-version"), "0.14.0\n");
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        var workspace = Read();
        Assert.Equal("local", workspace["ProviderId"]!.GetValue<string>());
        var channels = workspace["Channels"]!;
        var telegram = channels["Telegram"]!;
        Assert.Equal("channel-telegram", telegram["pluginId"]!.GetValue<string>());
        Assert.True(telegram["restoreChannelPlugin"]!.GetValue<bool>());
        Assert.Equal("existing-grant", channels["feishu"]!["pluginActivationId"]!.GetValue<string>());
        Assert.Equal("channel-weixin", channels["weixin"]!["pluginId"]!.GetValue<string>());
        Assert.Null(channels["weixin"]!["restoreChannelPlugin"]);
        Assert.Null(channels["qq"]);
        Assert.Null(channels["custom"]);
        Assert.Equal("""{ "telegram": { "botToken": "keep" } }""", File.ReadAllText(authFile));
    }

    [Fact]
    public void RetainsDockerOwnershipAndAppliesTheDeploymentSelection()
    {
        File.WriteAllText(WorkspaceConfig, """
            {
              "ExternalChannels": {
                "qq": { "enabled": true, "builtinModule": "channel-qq" },
                "telegram": { "enabled": true, "builtinModule": "channel-telegram" },
                "feishu": { "enabled": true, "transport": "websocket" }
              }
            }
            """);
        File.WriteAllText(Path.Combine(CraftPath, "docker-channels.json"), """{ "managed": ["wecom"] }""");

        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig, dockerSelection: "qq");

        var channels = Read()["Channels"]!;
        Assert.True(channels["qq"]!["enabled"]!.GetValue<bool>());
        Assert.False(channels["telegram"]!["enabled"]!.GetValue<bool>());
        Assert.Null(channels["feishu"]);
        var tracking = JsonNode.Parse(File.ReadAllText(Path.Combine(CraftPath, "docker-channels.json")))!;
        Assert.Equal(["qq", "telegram", "wecom"], tracking["managed"]!.AsArray().Select(n => n!.GetValue<string>()).Order());
    }

    [Fact]
    public void RetriesAnUnreadableConfigButDoesNotReapplyAfterSuccess()
    {
        File.WriteAllText(WorkspaceConfig, "{");
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);
        Assert.Equal("{", File.ReadAllText(WorkspaceConfig));

        const string legacy = """{ "ExternalChannels": { "feishu": { "builtinModule": "channel-feishu" } } }""";
        File.WriteAllText(WorkspaceConfig, legacy);
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);
        Assert.Equal("channel-feishu", Read()["Channels"]!["feishu"]!["pluginId"]!.GetValue<string>());

        File.WriteAllText(WorkspaceConfig, legacy);
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);
        Assert.Equal(legacy, File.ReadAllText(WorkspaceConfig));
    }

    [Fact]
    public void LeavesTheUserConfigAloneWhenItIsTheWorkspaceConfig()
    {
        const string config = """{ "ExternalChannels": { "wecom": { "builtinModule": "channel-wecom" } } }""";
        File.WriteAllText(WorkspaceConfig, config);

        WorkspaceConfigUpgrade.Apply(CraftPath, WorkspaceConfig);

        Assert.Equal(config, File.ReadAllText(WorkspaceConfig));
    }

    private JsonObject Read() => JsonNode.Parse(File.ReadAllText(WorkspaceConfig))!.AsObject();

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
