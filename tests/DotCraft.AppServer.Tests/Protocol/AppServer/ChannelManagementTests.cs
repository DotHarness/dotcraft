using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Workspaces;
using Xunit;
using Contract = DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ChannelManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-channel-wire-" + Guid.NewGuid().ToString("N"));
    private string CraftPath => Path.Combine(_root, ".craft");
    private string ConfigPath => Path.Combine(CraftPath, "config.json");

    [Fact]
    public async Task SetEnabledCommitsTheInstallationGrantBeforeApplyingRuntimeState()
    {
        CreateChannelPlugin();
        var applied = new List<ChannelEntry>();
        using var harness = CreateHarness(channel =>
        {
            Assert.Equal(channel, ReadSelection());
            applied.Add(channel);
        });
        var changes = new List<AppConfigChangedEventArgs>();
        harness.Monitor.Changed += (_, change) => changes.Add(change);
        using var init = await harness.InitializeAsync();
        var capabilities = init.RootElement.GetProperty("result").GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("channelManagement").GetBoolean());
        Assert.True(capabilities.GetProperty("pluginConfiguration").GetBoolean());

        using var enabled = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "example", enabled = true, pluginId = "channel-example" });
        AppServerTestHarness.AssertIsSuccessResponse(enabled);
        var running = Assert.Single(applied);
        Assert.True(running.Enabled);
        Assert.NotNull(running.PluginActivationId);
        Assert.Equal([ConfigChangeRegions.Channels], Assert.Single(changes).Regions);

        using var disabled = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "example", enabled = false, pluginId = "channel-example" });
        AppServerTestHarness.AssertIsSuccessResponse(disabled);
        Assert.Equal(2, applied.Count);
        Assert.False(ReadSelection().Enabled);
        Assert.Null(ReadSelection().PluginActivationId);
    }

    [Fact]
    public async Task AnUnavailableSelectionCanBeDisabledButCannotBeReplacedByAnUnknownPlugin()
    {
        Directory.CreateDirectory(CraftPath);
        File.WriteAllText(ConfigPath, """{"Channels":{"example":{"pluginId":"channel-example","enabled":true}}}""");
        using var harness = CreateHarness();
        using (await harness.InitializeAsync()) { }

        using var wrongPlugin = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "example", enabled = false, pluginId = "channel-unknown" });
        AppServerTestHarness.AssertIsErrorResponse(wrongPlugin, AppServerErrors.ChannelNotFoundCode);
        Assert.True(ReadSelection().Enabled);

        using var disabled = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "example", enabled = false, pluginId = "channel-example" });
        AppServerTestHarness.AssertIsSuccessResponse(disabled);
        Assert.False(ReadSelection().Enabled);
    }

    [Fact]
    public async Task SetEnabledRejectsLaunchCommandsAndConflictsWithSessionOrigins()
    {
        CreateChannelPlugin();
        using var harness = CreateHarness(channelListContributor: new ReservedOrigin());
        using (await harness.InitializeAsync()) { }

        using var unsafeInput = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "example", enabled = true, pluginId = "channel-example", command = "arbitrary" });
        AppServerTestHarness.AssertIsErrorResponse(unsafeInput, AppServerErrors.ChannelValidationFailedCode);

        using var conflict = await CallAsync(harness, Contract.AppServerRpc.ChannelSetEnabled.Name,
            new { name = "cli", enabled = true, pluginId = "channel-example" });
        AppServerTestHarness.AssertIsErrorResponse(conflict, AppServerErrors.ChannelNameConflictCode);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public async Task ManagementRequiresAWorkspaceRuntimeAndDoesNotAuthorizeChannelAdapters()
    {
        Directory.CreateDirectory(CraftPath);
        using var unsupported = new AppServerTestHarness(workspaceCraftPath: CraftPath);
        using (await unsupported.InitializeAsync()) { }
        using var unsupportedRead = await CallAsync(unsupported, Contract.AppServerRpc.ChannelLogs.Name, new { name = "example" });
        AppServerTestHarness.AssertIsErrorResponse(unsupportedRead, AppServerErrors.MethodNotFoundCode);

        using var adapter = CreateHarness();
        using var init = await CallAsync(adapter, Contract.AppServerRpc.Initialize.Name, new
        {
            clientInfo = new { name = "example-adapter", version = "1.0.0" },
            capabilities = new
            {
                channelAdapter = new
                {
                    channelName = "example",
                    displayName = "Example",
                    deliveryCapabilities = new { text = new { format = "plain" }, targets = new { group = false, direct = "knownOnly" } }
                }
            }
        });
        AppServerTestHarness.AssertIsSuccessResponse(init);
        Assert.False(init.RootElement.GetProperty("result").GetProperty("capabilities").TryGetProperty("channelManagement", out _));
        adapter.Handler.HandleInitializedNotification();
        using var restrictedRead = await CallAsync(adapter, Contract.AppServerRpc.ChannelSetupRead.Name, new { pluginId = "channel-example" });
        AppServerTestHarness.AssertIsErrorResponse(restrictedRead, AppServerErrors.InvalidRequestCode);
    }

    private AppServerTestHarness CreateHarness(
        Action<ChannelEntry>? onApply = null,
        IAppServerChannelListContributor? channelListContributor = null)
    {
        var config = new AppConfig();
        config.Plugins.DisableDefaultPluginRegistry = true;
        var plugins = new ChannelPluginWorkspace(DotCraftPaths.Create(_root, CraftPath), config, "0.14.0");
        return new AppServerTestHarness(
            workspaceCraftPath: CraftPath,
            onChannelSetEnabled: (channel, _) =>
            {
                onApply?.Invoke(channel);
                return Task.CompletedTask;
            },
            channelRuntimeProvider: new TestRuntime(plugins),
            channelListContributor: channelListContributor);
    }

    private void CreateChannelPlugin()
    {
        var plugin = Path.Combine(CraftPath, "plugins", "channel-example");
        var metadata = Directory.CreateDirectory(Path.Combine(plugin, ".craft-plugin"));
        File.WriteAllText(Path.Combine(metadata.FullName, "plugin.json"), """
            {
              "schemaVersion":1,"id":"channel-example","displayName":"Example","capabilities":["channel"],
              "channel":{"manifest":"./manifest.json","entry":"./entry.mjs","minHostVersion":"0.14.0"}
            }
            """);
        File.WriteAllText(Path.Combine(plugin, "manifest.json"), """
            {
              "moduleId":"example-standard","channelName":"example","displayName":"Example",
              "packageName":"example-channel","variant":"standard","configFileName":"example.json",
              "supportedTransports":["websocket"],"channelContractVersion":"1.0.0","supportedChannelProtocolVersions":["0.2"],
              "requiresInteractiveSetup":false,"capabilitySummary":{
                "requiresInteractiveSetup":false,"hasChannelTools":false,"capabilitySetMayVaryByEnvironment":false
              },
              "launcher":{"supportsWorkspaceFlag":true}
            }
            """);
        File.WriteAllText(Path.Combine(plugin, "entry.mjs"), "export {};");
    }

    private ChannelEntry ReadSelection() => Assert.Single(AppConfig.Load(ConfigPath).Channels);

    private static async Task<JsonDocument> CallAsync(AppServerTestHarness harness, string method, object parameters)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
        return await harness.Transport.ReadNextSentAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class ReservedOrigin : IAppServerChannelListContributor
    {
        public void AppendBaseChannels(List<ChannelDescriptor> channels, HashSet<string> seen)
        {
            if (seen.Add("cli"))
                channels.Add(new ChannelDescriptor { Name = "cli", Category = "builtin" });
        }
    }

    private sealed class TestRuntime(ChannelPluginWorkspace plugins) : IChannelRuntimeProvider
    {
        public ChannelPluginWorkspace ChannelPlugins => plugins;
        public Contract.ChannelSetupArtifact? GetChannelSetupArtifact(string pluginId) => null;
        public IReadOnlyList<string> GetRecentChannelLogs(string channelName, int? tail = null) => [];
        public Task<IReadOnlyList<string>?> GetConfigOptionAvailabilityAsync(string channelName, string key, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>?>(null);
    }
}
