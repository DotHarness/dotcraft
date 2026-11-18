using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task PluginInstall_SelectsTheMarketplaceEntryAndKeepsTheOtherListed()
    {
        var config = new AppConfig();
        config.Plugins.DisableDefaultPluginRegistry = true;
        foreach (var marketplace in new[] { "alpha", "beta" })
            ConfigureSharedPluginMarketplace(config, marketplace);
        using var harness = CreateHarness(config, includeBundledRoots: false);
        using (await harness.InitializeAsync()) { }

        var before = await ReadPluginListResultAsync(harness);
        Assert.Equal(["shared@alpha", "shared@beta"], SharedKeys(before));

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall,
            new { id = "shared", marketplaceName = "beta" }));
        using (var installed = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(installed);
            var plugin = installed.RootElement.GetProperty("result").GetProperty("plugin");
            Assert.Equal("shared@beta", plugin.GetProperty("key").GetString());
            Assert.True(plugin.GetProperty("installed").GetBoolean());
        }
        using (await harness.Transport.ReadNextSentAsync()) { }

        var after = await ReadPluginListResultAsync(harness);
        var other = Assert.Single(
            after.GetProperty("plugins").EnumerateArray(),
            plugin => plugin.GetProperty("key").GetString() == "shared@alpha");
        Assert.False(other.TryGetProperty("installed", out var otherInstalled) && otherInstalled.GetBoolean());
        Assert.False(other.TryGetProperty("installable", out var otherInstallable) && otherInstallable.GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall,
            new { id = "shared", marketplaceName = "alpha" }));
        using var rejected = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task PluginView_SelectsTheBundledEntryWhileAMarketplaceCopyIsInstalled()
    {
        WriteSkillPlugin(Path.Combine(_bundledPluginSourceRoot, "shared"), "shared", "bundled-skill");
        var config = new AppConfig();
        config.Plugins.DisableDefaultPluginRegistry = true;
        ConfigureSharedPluginMarketplace(config, "beta");
        using var harness = CreateHarness(config);
        using (await harness.InitializeAsync()) { }

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall,
            new { id = "shared", marketplaceName = "beta" }));
        using (await harness.Transport.ReadNextSentAsync()) { }
        using (await harness.Transport.ReadNextSentAsync()) { }

        Assert.Equal(["shared@beta", "shared@dotcraft-bundled"], SharedKeys(await ReadPluginListResultAsync(harness)));
        Assert.Equal("shared@dotcraft-bundled", await ViewKeyAsync(harness, new { id = "shared", marketplaceName = "dotcraft-bundled" }));
        Assert.Equal("shared@beta", await ViewKeyAsync(harness, new { id = "shared" }));
    }

    private static async Task<string?> ViewKeyAsync(AppServerTestHarness harness, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginView,
            parameters));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("key").GetString();
    }

    private void ConfigureSharedPluginMarketplace(AppConfig config, string marketplace)
    {
        var root = Path.Combine(_tempRoot, "marketplaces", marketplace);
        Directory.CreateDirectory(Path.Combine(root, ".craft", "plugins"));
        File.WriteAllText(Path.Combine(root, ".craft", "plugins", "marketplace.json"), $$"""
            {
              "name": "{{marketplace}}",
              "plugins": [
                {
                  "name": "shared",
                  "source": { "source": "local", "path": "./plugins/shared" },
                  "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                  "category": "Testing"
                }
              ]
            }
            """);
        WriteSkillPlugin(Path.Combine(root, "plugins", "shared"), "shared", $"{marketplace}-skill");
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
        {
            Name = marketplace,
            SourceType = "local",
            Url = root
        });
    }

    private static string?[] SharedKeys(JsonElement list) =>
        list.GetProperty("plugins").EnumerateArray()
            .Where(plugin => plugin.GetProperty("id").GetString() == "shared")
            .Select(plugin => plugin.GetProperty("key").GetString())
            .Order(StringComparer.Ordinal)
            .ToArray();
}
