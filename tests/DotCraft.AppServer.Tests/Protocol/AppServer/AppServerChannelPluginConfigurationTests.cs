using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    private const string ChannelSettingsPlugin = "channel-config-sample";
    private const string PlatformConfigFile = "example-channel.json";

    [Fact]
    public async Task ChannelSettings_ReusePluginApiWithoutSharingOrReturningCredentials()
    {
        WriteChannelSettingsFixture();
        var secondCraftPath = Path.Combine(_tempRoot, "other-workspace", ".craft");
        Directory.CreateDirectory(secondCraftPath);
        var platformConfigPath = Path.Combine(_workspaceCraftPath, PlatformConfigFile);
        File.WriteAllText(platformConfigPath, """
            {"example":{"token":"original-fixture-token","accounts":[{"secret":"nested-fixture-token"}],"extra":"preserve"},
             "dotcraft":{"token":"host-fixture-token"}}
            """);
        using var first = CreateChannelSettingsHarness(_workspaceCraftPath);
        using var second = CreateChannelSettingsHarness(secondCraftPath);
        using var firstInit = await first.InitializeAsync();
        using var secondInit = await second.InitializeAsync();
        Assert.True(firstInit.RootElement.GetProperty("result").GetProperty("capabilities").GetProperty("pluginConfiguration").GetBoolean());

        using var initial = await GetChannelSettingsAsync(first);
        AppServerTestHarness.AssertIsSuccessResponse(initial);
        var snapshot = initial.RootElement.GetProperty("result");
        Assert.Equal("safe", snapshot.GetProperty("value").GetProperty("example.mode").GetString());
        Assert.Empty(snapshot.GetProperty("workspace").EnumerateObject());
        Assert.Empty(snapshot.GetProperty("personal").EnumerateObject());
        Assert.Equal("workspace", Assert.Single(snapshot.GetProperty("writableScopes").EnumerateArray()).GetString());
        Assert.Equal(["example.token", "example.accounts"],
            snapshot.GetProperty("configuredSecrets").EnumerateArray().Select(key => key.GetString()));
        Assert.DoesNotContain("fixture-token", snapshot.GetRawText());
        Assert.DoesNotContain("secret-default-fixture", snapshot.GetRawText());
        Assert.DoesNotContain("example.loginState", snapshot.GetRawText());

        var changes = new List<AppConfigChangedEventArgs>();
        first.Monitor.Changed += (_, args) => changes.Add(args);
        using var saved = await MutateChannelSettingsAsync(first, "workspace",
            new { op = "set", key = "EXAMPLE.TOKEN", value = "replacement-fixture-token" },
            new { op = "unset", key = "example.accounts" });
        AppServerTestHarness.AssertIsSuccessResponse(saved);
        var after = saved.RootElement.GetProperty("result");
        Assert.Equal("example.token", Assert.Single(after.GetProperty("configuredSecrets").EnumerateArray()).GetString());
        Assert.DoesNotContain("replacement-fixture-token", saved.RootElement.GetRawText());
        Assert.Equal(AppServerMethodNames.PluginConfigMutate, Assert.Single(changes).Source);
        Assert.Equal([ConfigChangeRegions.PluginConfiguration], Assert.Single(changes).Regions);

        using var persisted = JsonDocument.Parse(File.ReadAllText(platformConfigPath));
        var example = persisted.RootElement.GetProperty("example");
        Assert.Equal("replacement-fixture-token", example.GetProperty("token").GetString());
        Assert.Equal("preserve", example.GetProperty("extra").GetString());
        Assert.False(example.TryGetProperty("mode", out _));
        Assert.False(example.TryGetProperty("accounts", out _));
        Assert.Equal("host-fixture-token", persisted.RootElement.GetProperty("dotcraft").GetProperty("token").GetString());
        Assert.False(File.Exists(Path.Combine(_workspaceCraftPath, PluginConfigStore.FileName)));

        using var independent = await GetChannelSettingsAsync(second);
        AppServerTestHarness.AssertIsSuccessResponse(independent);
        Assert.Empty(independent.RootElement.GetProperty("result").GetProperty("configuredSecrets").EnumerateArray());
        var secondConfig = Path.Combine(secondCraftPath, PlatformConfigFile);
        Assert.False(File.Exists(secondConfig));
        File.WriteAllText(secondConfig, """{"example":{"mode":null}}""");
        using var invalidOverride = await GetChannelSettingsAsync(second);
        AppServerTestHarness.AssertIsSuccessResponse(invalidOverride);
        Assert.False(invalidOverride.RootElement.GetProperty("result").GetProperty("value").TryGetProperty("example.mode", out _));
    }

    [Fact]
    public async Task ChannelSettings_RejectPersonalScopeAndInvalidBatchesBeforeWriting()
    {
        WriteChannelSettingsFixture();
        using var harness = CreateChannelSettingsHarness(_workspaceCraftPath);
        using var initialized = await harness.InitializeAsync();
        using var personal = await MutateChannelSettingsAsync(harness, "personal",
            new { op = "set", key = "example.token", value = "fixture-token" });
        AssertPluginConfigurationError(personal, PluginConfigStore.ScopeUnavailable);

        foreach (var invalid in new object[]
        {
            new { op = "set", key = "dotcraft.token", value = "fixture-token" },
            new { op = "set", key = "example.loginState", value = "fixture-token" },
            new { op = "set", key = "example.mode", value = "unknown" },
            new { op = "set", key = "example.accounts", value = "not an array" },
        })
        {
            using var rejected = await MutateChannelSettingsAsync(harness, "workspace",
                new { op = "set", key = "example.mode", value = "fast" }, invalid);
            AssertPluginConfigurationError(rejected, PluginConfigStore.MutationInvalid);
        }

        Assert.False(File.Exists(Path.Combine(_userDataPath, PluginConfigStore.FileName)));
        Assert.False(File.Exists(Path.Combine(_workspaceCraftPath, PlatformConfigFile)));
    }

    [Fact]
    public async Task ChannelSettings_RejectOversizedStoredJsonBeforeReplacingSavedSettings()
    {
        WriteChannelSettingsFixture();
        File.WriteAllText(Path.Combine(_workspaceCraftPath, PlatformConfigFile), """{"example":{"mode":"fast"}}""");
        using var harness = CreateChannelSettingsHarness(_workspaceCraftPath);
        using var initialized = await harness.InitializeAsync();

        using var rejected = await MutateChannelSettingsAsync(harness, "workspace",
            new { op = "set", key = "example.accounts", value = Enumerable.Repeat("abc", 100_000).ToArray() },
            new { op = "set", key = "example.mode", value = "safe" });
        AssertPluginConfigurationError(rejected, PluginConfigStore.MutationInvalid);

        using var read = await GetChannelSettingsAsync(harness);
        AppServerTestHarness.AssertIsSuccessResponse(read);
        var saved = read.RootElement.GetProperty("result");
        Assert.Equal("fast", saved.GetProperty("workspace").GetProperty("example.mode").GetString());
        Assert.Empty(saved.GetProperty("configuredSecrets").EnumerateArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ChannelSettings_AreNotAccessibleFromAdapterOrAppPrincipal(bool adapter)
    {
        WriteChannelSettingsFixture();
        using var harness = CreateChannelSettingsHarness(_workspaceCraftPath);
        if (adapter)
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.Initialize, new
            {
                clientInfo = new { name = "fixture-adapter", version = "1.0.0" },
                capabilities = new
                {
                    channelAdapter = new
                    {
                        channelName = "example",
                        displayName = "Example",
                        deliveryCapabilities = new { text = new { format = "plain" }, targets = new { group = false, direct = "any" } }
                    }
                }
            }));
            using var initialized = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(initialized);
            Assert.True(harness.Handler.HandleNotification(harness.BuildNotification(AppServerMethodNames.Initialized, new { })));
        }
        else
        {
            using var initialized = await harness.InitializeAsync();
            harness.Connection.BindAppPrincipal("fixture-principal", "fixture-app");
        }

        var expected = adapter ? AppServerErrors.InvalidRequestCode : AppServerErrors.AppPrincipalUnauthorizedCode;
        using var read = await GetChannelSettingsAsync(harness);
        AppServerTestHarness.AssertIsErrorResponse(read, expected);
        using var write = await MutateChannelSettingsAsync(harness, "workspace",
            new { op = "set", key = "example.token", value = "fixture-token" });
        AppServerTestHarness.AssertIsErrorResponse(write, expected);
        Assert.False(File.Exists(Path.Combine(_workspaceCraftPath, PlatformConfigFile)));
    }

    private AppServerTestHarness CreateChannelSettingsHarness(string craftPath)
    {
        var config = new AppConfig { GlobalConfigPath = Path.Combine(_userDataPath, "config.json") };
        var paths = DotCraftPaths.Create(Path.GetDirectoryName(craftPath)!, craftPath, _userDataPath);
        return new AppServerTestHarness(
            workspaceCraftPath: craftPath,
            appConfigMonitor: new AppConfigMonitor(config),
            pluginConfigStore: new PluginConfigStore(paths),
            channelRuntimeProvider: new SettingsChannelRuntime(new ChannelPluginWorkspace(paths, config, "1.0.0")));
    }

    private static async Task<JsonDocument> GetChannelSettingsAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.PluginConfigGet, new { id = ChannelSettingsPlugin }));
        return await harness.Transport.ReadNextSentAsync();
    }

    private static async Task<JsonDocument> MutateChannelSettingsAsync(
        AppServerTestHarness harness, string scope, params object[] operations)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethodNames.PluginConfigMutate, new { id = ChannelSettingsPlugin, scope, operations }));
        return await harness.Transport.ReadNextSentAsync();
    }

    private static void AssertPluginConfigurationError(JsonDocument doc, string code)
    {
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.PluginConfigurationErrorCode);
        Assert.Equal(code, doc.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString());
    }

    private void WriteChannelSettingsFixture()
    {
        var root = Path.Combine(_userDataPath, "plugins", ChannelSettingsPlugin);
        Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        File.WriteAllText(Path.Combine(root, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1, "id": "{{ChannelSettingsPlugin}}", "displayName": "Example",
              "capabilities": ["channel"],
              "channel": { "manifest": "./channel.json", "entry": "./entry.mjs", "minHostVersion": "1.0.0" }
            }
            """);
        File.WriteAllText(Path.Combine(root, "entry.mjs"), "throw new Error('Not run while editing settings');");
        File.WriteAllText(Path.Combine(root, "channel.json"), $$"""
            {
              "moduleId": "example-standard", "channelName": "example", "displayName": "Example",
              "packageName": "@example/channel", "configFileName": "{{PlatformConfigFile}}", "variant": "standard",
              "channelContractVersion": "1.0.0", "supportedChannelProtocolVersions": ["0.2"],
              "supportedTransports": ["websocket"], "requiresInteractiveSetup": true,
              "capabilitySummary": {
                "hasChannelTools": false, "requiresInteractiveSetup": true, "capabilitySetMayVaryByEnvironment": false
              },
              "launcher": { "bin": "example", "supportsWorkspaceFlag": true },
              "configDescriptors": [
                { "key": "example.token", "displayLabel": "Token", "dataKind": "secret",
                  "required": true, "masked": true, "interactiveSetupOnly": false, "defaultValue": "secret-default-fixture" },
                { "key": "example.accounts", "displayLabel": "Accounts", "dataKind": "list",
                  "required": false, "masked": true, "interactiveSetupOnly": false },
                { "key": "example.mode", "displayLabel": "Mode", "dataKind": "enum",
                  "required": true, "masked": false, "interactiveSetupOnly": false,
                  "defaultValue": "safe", "enumValues": ["safe", "fast"] },
                { "key": "example.loginState", "displayLabel": "Login", "dataKind": "string",
                  "required": true, "masked": false, "interactiveSetupOnly": true },
                { "key": "dotcraft.token", "displayLabel": "Host token", "dataKind": "secret",
                  "required": false, "masked": true, "interactiveSetupOnly": false }
              ]
            }
            """);
    }

    private sealed class SettingsChannelRuntime(ChannelPluginWorkspace workspace) : IChannelRuntimeProvider
    {
        public ChannelPluginWorkspace ChannelPlugins => workspace;

        public ChannelSetupArtifact? GetChannelSetupArtifact(string pluginId) => null;

        public IReadOnlyList<string> GetRecentChannelLogs(string channelName, int? tail) => [];

        public Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(
            string channelName, string key, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
