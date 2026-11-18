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
    public void RemovesPersonalSettingsAndKeepsProjectData()
    {
        Write(UserConfig, """{ "ProviderId": "global", "ProviderPreferences": { "global": { "model": "g" } } }""");
        Write(WorkspaceConfig, """
            {
              "providerid": "local",
              "ProviderPreferences": { "local": { "model": "l" } },
              "Permissions": { "DefaultApprovalPolicy": "autoApprove" },
              "Tools": { "Web": { "SearchProvider": "Exa", "ExaApiKey": "secret" } },
              "SubAgent": { "DisabledProfiles": ["a"], "MaxDepth": 2 },
              "Plugins": { "DisabledPlugins": ["p"] },
              "McpServers": { "s": { "command": "x" } }
            }
            """);

        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        var workspace = Read(WorkspaceConfig);
        Assert.Null(workspace["providerid"]);
        Assert.Null(workspace["ProviderPreferences"]);
        Assert.Null(workspace["Permissions"]);
        Assert.Null(workspace["Tools"]!["Web"]!["SearchProvider"]);
        Assert.Equal("secret", workspace["Tools"]!["Web"]!["ExaApiKey"]!.GetValue<string>());
        Assert.Null(workspace["SubAgent"]!["DisabledProfiles"]);
        Assert.Equal(2, workspace["SubAgent"]!["MaxDepth"]!.GetValue<int>());
        Assert.NotNull(workspace["Plugins"]);
        Assert.NotNull(workspace["McpServers"]);
        Assert.Equal("global", Read(UserConfig)["ProviderId"]!.GetValue<string>());
    }

    [Fact]
    public void MovesTheProviderSelectionWhenTheUserLayerHasNone()
    {
        Write(UserConfig, """{ "Providers": { "local": {} } }""");
        Write(WorkspaceConfig, """{ "ProviderId": "local", "ProviderPreferences": { "local": { "model": "l" }, "other": { "model": "o" } } }""");

        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        var user = Read(UserConfig);
        Assert.Equal("local", user["ProviderId"]!.GetValue<string>());
        Assert.Equal("l", user["ProviderPreferences"]!["local"]!["model"]!.GetValue<string>());
        Assert.Null(user["ProviderPreferences"]!["other"]);
        Assert.Null(Read(WorkspaceConfig)["ProviderId"]);
    }

    [Fact]
    public void RunsOnceSoLaterWorkspaceSettingsSurvive()
    {
        Write(WorkspaceConfig, """{ "Permissions": { "DefaultApprovalPolicy": "prompt" } }""");
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        Write(WorkspaceConfig, """{ "ProviderId": "pinned", "Permissions": { "DefaultApprovalPolicy": "autoApprove" } }""");
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        var workspace = Read(WorkspaceConfig);
        Assert.Equal("pinned", workspace["ProviderId"]!.GetValue<string>());
        Assert.Equal("autoApprove", workspace["Permissions"]!["DefaultApprovalPolicy"]!.GetValue<string>());
    }

    [Fact]
    public void LeavesAnUnreadableWorkspaceConfigForTheNextStart()
    {
        const string broken = """{ "ProviderId": """;
        Write(WorkspaceConfig, broken);

        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);

        Assert.Equal(broken, File.ReadAllText(WorkspaceConfig));
        Write(WorkspaceConfig, """{ "Memory": { "Enabled": false } }""");
        WorkspaceConfigUpgrade.Apply(CraftPath, UserConfig);
        Assert.Null(Read(WorkspaceConfig)["Memory"]);
    }

    [Fact]
    public void LeavesTheUserConfigAloneWhenItIsTheWorkspaceConfig()
    {
        Write(WorkspaceConfig, """{ "ProviderId": "global", "Permissions": { "DefaultApprovalPolicy": "prompt" } }""");

        WorkspaceConfigUpgrade.Apply(CraftPath, WorkspaceConfig);

        var config = Read(WorkspaceConfig);
        Assert.Equal("global", config["ProviderId"]!.GetValue<string>());
        Assert.NotNull(config["Permissions"]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    private static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
}
