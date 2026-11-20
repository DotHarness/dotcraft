using System.IO.Compression;
using System.Net;
using System.Text.Json;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Skills;
using Contract = DotCraft.Protocol.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerStartupPluginReconcileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"plugin_startup_reconcile_{Guid.NewGuid():N}");
    private string Sources => Path.Combine(_root, "sources");
    private string UserData => Path.Combine(_root, "user");
    private string Installed => Path.Combine(UserData, "plugins");
    private string WorkspaceCraftPath => Path.Combine(_root, "workspace", ".craft");

    [Fact]
    public async Task RunStartupSync_UpdatesInstalledBuiltInPlugins_PreservesDisabled_SkipsUninstalledAndUserOwned()
    {
        WriteBuiltInPlugin("active", "old");
        WriteBuiltInPlugin("disabled-demo", "old");
        WriteBuiltInPlugin("uninstalled", "old");
        WriteBuiltInPlugin("user-owned", "bundled-old");

        var pluginsConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            DisabledPlugins = ["disabled-demo"]
        };
        new BuiltInPluginDeployer(Installed, [Sources], pluginsConfig, UserData)
            .DeployPlugins(["active@dotcraft-bundled", "disabled-demo@dotcraft-bundled"]);
        WriteUserOwnedPlugin("user-owned", "custom-user-content");

        WriteBuiltInPlugin("active", "new");
        WriteBuiltInPlugin("disabled-demo", "new");
        WriteBuiltInPlugin("uninstalled", "new");
        WriteBuiltInPlugin("user-owned", "bundled-new");

        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(pluginsConfig, managementState);
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, [Sources]);
        var syncService = new PluginRegistrySyncService(UserData, handler: new StubHandler(HttpStatusCode.NotFound));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.True(notification.SnapshotRevision.Value > 0);
        Assert.Equal(["active", "disabled-demo"], notification.PluginIds.Value);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "active", "content.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "disabled-demo", "content.txt")));
        Assert.False(Directory.Exists(Path.Combine(Installed, "uninstalled")));
        Assert.Equal("custom-user-content", File.ReadAllText(Path.Combine(Installed, "user-owned", "content.txt")));
        Assert.True(File.Exists(Path.Combine(UserData, "imports", "revision.json")));

        using var list = await ListPluginsAsync(harness);
        var active = FindPlugin(list, "active");
        Assert.Equal("new", active.GetProperty("displayName").GetString());
        Assert.True(active.GetProperty("enabled").GetBoolean());

        var disabled = FindPlugin(list, "disabled-demo");
        Assert.Equal("new", disabled.GetProperty("displayName").GetString());
        Assert.False(disabled.GetProperty("enabled").GetBoolean());

        var uninstalled = FindPlugin(list, "uninstalled");
        Assert.False(uninstalled.GetProperty("installed").GetBoolean());

        var userOwned = FindInstalledPlugin(list, "user-owned");
        Assert.Equal("custom-user-content", userOwned.GetProperty("displayName").GetString());
        Assert.True(userOwned.GetProperty("installed").GetBoolean());

        notifications.Clear();
        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task RunStartupSync_PartialFailureKeepsBlockedPluginAndUpdatesHealthyPlugin()
    {
        WriteBuiltInPlugin("blocked", "old");
        WriteBuiltInPlugin("working", "old");
        var pluginsConfig = new AppConfig.PluginsConfig { DisableDefaultPluginRegistry = true };
        new BuiltInPluginDeployer(Installed, [Sources], pluginsConfig, UserData).Deploy();

        WriteBuiltInPlugin("blocked", "new");
        WriteBuiltInPlugin("working", "new");

        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(pluginsConfig, managementState);
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, [Sources]);
        var syncService = new PluginRegistrySyncService(UserData, handler: new StubHandler(HttpStatusCode.NotFound));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        using var occupied = File.Open(
            Path.Combine(OperatingSystem.IsWindows() ? Installed : Sources, "blocked", "content.txt"),
            FileMode.Open, FileAccess.Read, OperatingSystem.IsWindows() ? FileShare.Read : FileShare.None);

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.Equal(["working"], notification.PluginIds.Value);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "blocked", "content.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "working", "content.txt")));
    }

    [Fact]
    public async Task RunStartupSync_WhenArchiveRegistryActivatesNewSnapshot_UpdatesInstalledPluginAndBroadcasts()
    {
        const string registryUrl = "https://registry.test/official.zip";
        const string marketplaceName = "official-test";
        const string marketplacePath = ".craft/plugins/marketplace.json";

        var pluginsConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = marketplaceName,
                    SourceType = "archive",
                    Url = registryUrl,
                    MarketplacePath = marketplacePath
                }
            ]
        };

        var cache = new PluginRegistryArchiveCache(UserData);
        cache.Activate(registryUrl, marketplacePath, CreateRegistryArchive(marketplaceName, "registry-demo", "old"));
        new BuiltInPluginDeployer(Installed, [], pluginsConfig, UserData)
            .DeployPlugin($"registry-demo@{marketplaceName}");
        Assert.Equal("old", File.ReadAllText(Path.Combine(Installed, "registry-demo", "content.txt")));

        cache.Invalidate(registryUrl, marketplacePath);

        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(pluginsConfig, managementState, includeBundledRoots: false);
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, []);
        var syncService = new PluginRegistrySyncService(
            UserData,
            handler: new StubHandler(HttpStatusCode.OK, CreateRegistryArchive(marketplaceName, "registry-demo", "new")));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.Equal(["registry-demo"], notification.PluginIds.Value);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "registry-demo", "content.txt")));
    }

    [Fact]
    public async Task RunStartupSync_WhenArchiveRegistryActivatesWithoutInstalledUpdates_BroadcastsCatalogReload()
    {
        const string registryUrl = "https://registry.test/catalog-only.zip";
        const string marketplaceName = "catalog-only";
        const string marketplacePath = ".craft/plugins/marketplace.json";

        var pluginsConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = marketplaceName,
                    SourceType = "archive",
                    Url = registryUrl,
                    MarketplacePath = marketplacePath
                }
            ]
        };

        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(pluginsConfig, managementState, includeBundledRoots: false);
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, []);
        var syncService = new PluginRegistrySyncService(
            UserData,
            handler: new StubHandler(HttpStatusCode.OK, CreateRegistryArchive(marketplaceName, "catalog-demo", "v1")));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.NotNull(notification.PluginIds.Value);
        Assert.Empty(notification.PluginIds.Value);
        Assert.False(Directory.Exists(Path.Combine(Installed, "catalog-demo")));
    }

    [Fact]
    public async Task RunStartupSync_PreservesWorkspaceEmptyRegistriesAndExpandedEnvUrls()
    {
        var globalMarketplaceRoot = Path.Combine(_root, "global-mp");
        WriteLocalMarketplace(globalMarketplaceRoot, "global-mp", "global-demo", "v1");
        var globalConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = "global-mp",
                    SourceType = "local",
                    Url = globalMarketplaceRoot
                }
            ]
        };
        new BuiltInPluginDeployer(Installed, [], globalConfig, UserData).DeployPlugin("global-demo@global-mp");
        WriteLocalMarketplace(globalMarketplaceRoot, "global-mp", "global-demo", "v2");

        var excludedConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries = []
        };
        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(excludedConfig, managementState, includeBundledRoots: false);
        File.WriteAllText(
            Path.Combine(UserData, "config.json"),
            JsonSerializer.Serialize(new { Plugins = new { globalConfig.PluginRegistries } }));
        File.WriteAllText(
            Path.Combine(WorkspaceCraftPath, "config.json"),
            """{"Plugins":{"PluginRegistries":[]}}""");
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, []);
        var syncService = new PluginRegistrySyncService(UserData, handler: new StubHandler(HttpStatusCode.NotFound));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        Assert.Empty(notifications);
        Assert.Equal("v1", File.ReadAllText(Path.Combine(Installed, "global-demo", "content.txt")));

        const string expandedUrl = "https://registry.test/expanded.zip";
        const string marketplacePath = ".craft/plugins/marketplace.json";
        var expandedConfig = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig
                {
                    Name = "env-mp",
                    SourceType = "archive",
                    Url = expandedUrl,
                    MarketplacePath = marketplacePath
                }
            ]
        };
        var cache = new PluginRegistryArchiveCache(UserData);
        cache.Activate(expandedUrl, marketplacePath, CreateRegistryArchive("env-mp", "env-demo", "old"));
        new BuiltInPluginDeployer(Installed, [], expandedConfig, UserData).DeployPlugin("env-demo@env-mp");
        cache.Invalidate(expandedUrl, marketplacePath);

        File.WriteAllText(
            Path.Combine(WorkspaceCraftPath, "config.json"),
            """{"Plugins":{"PluginRegistries":[{"Name":"env-mp","SourceType":"archive","Url":"${REGISTRY_URL}"}]}}""");
        harness.Monitor.Current.Plugins = expandedConfig;

        var expandedSyncService = new PluginRegistrySyncService(
            UserData,
            handler: new StubHandler(HttpStatusCode.OK, CreateRegistryArchive("env-mp", "env-demo", "new")));
        await refresher.RunStartupSyncAsync(UserData, expandedSyncService, notifications.Add, CancellationToken.None);

        var notification = Assert.Single(notifications);
        Assert.Equal(["env-demo"], notification.PluginIds.Value);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Installed, "env-demo", "content.txt")));
    }

    [Fact]
    public async Task RunStartupSync_SkipsMarketplacePluginWhenActiveSourceDiffersFromInstalledSource()
    {
        var marketplaceA = Path.Combine(_root, "mp-a");
        var marketplaceB = Path.Combine(_root, "mp-b");
        WriteLocalMarketplace(marketplaceA, "team-mp", "shared-demo", "from-a");
        WriteLocalMarketplace(marketplaceB, "team-mp", "shared-demo", "from-b");

        var configA = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig { Name = "team-mp", SourceType = "local", Url = marketplaceA }
            ]
        };
        new BuiltInPluginDeployer(Installed, [], configA, UserData).DeployPlugin("shared-demo@team-mp");

        var configB = new AppConfig.PluginsConfig
        {
            DisableDefaultPluginRegistry = true,
            PluginRegistries =
            [
                new AppConfig.PluginRegistryConfig { Name = "team-mp", SourceType = "local", Url = marketplaceB }
            ]
        };
        var managementState = new AppServerPluginManagementState();
        using var harness = CreateHarness(configB, managementState, includeBundledRoots: false);
        using (await harness.InitializeAsync()) { }

        var refresher = CreateRefresher(harness, managementState, []);
        var syncService = new PluginRegistrySyncService(UserData, handler: new StubHandler(HttpStatusCode.NotFound));
        var notifications = new List<Contract.PluginSnapshotUpdatedNotification>();

        await refresher.RunStartupSyncAsync(UserData, syncService, notifications.Add, CancellationToken.None);

        Assert.Empty(notifications);
        Assert.Equal("from-a", File.ReadAllText(Path.Combine(Installed, "shared-demo", "content.txt")));
    }

    private static void WriteLocalMarketplace(string root, string marketplaceName, string pluginId, string content)
    {
        Directory.CreateDirectory(Path.Combine(root, ".craft", "plugins"));
        File.WriteAllText(Path.Combine(root, ".craft", "plugins", "marketplace.json"), $$"""
            {
              "name": "{{marketplaceName}}",
              "plugins": [
                {
                  "name": "{{pluginId}}",
                  "source": { "source": "local", "path": "./plugins/{{pluginId}}" },
                  "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                  "category": "Testing"
                }
              ]
            }
            """);
        WritePlugin(Path.Combine(root, "plugins", pluginId), pluginId, content);
    }

    private AppServerTestHarness CreateHarness(
        AppConfig.PluginsConfig pluginsConfig,
        AppServerPluginManagementState managementState,
        bool includeBundledRoots = true)
    {
        Directory.CreateDirectory(WorkspaceCraftPath);
        Directory.CreateDirectory(UserData);
        File.WriteAllText(
            Path.Combine(UserData, "config.json"),
            JsonSerializer.Serialize(new { Plugins = new { pluginsConfig.PluginRegistries } }));
        File.WriteAllText(
            Path.Combine(WorkspaceCraftPath, "config.json"),
            JsonSerializer.Serialize(new { Plugins = new { pluginsConfig.DisabledPlugins } }));

        var config = new AppConfig
        {
            GlobalConfigPath = Path.Combine(UserData, "config.json"),
            Plugins = pluginsConfig
        };
        return new AppServerTestHarness(
            workspaceCraftPath: WorkspaceCraftPath,
            skillsLoader: new SkillsLoader(WorkspaceCraftPath),
            appConfigMonitor: new AppConfigMonitor(config),
            builtInPluginSourceRoots: includeBundledRoots ? [Sources] : [],
            pluginManagementState: managementState);
    }

    private AppServerManagedPluginRefresher CreateRefresher(
        AppServerTestHarness harness,
        AppServerPluginManagementState managementState,
        IReadOnlyList<string> builtInRoots)
    {
        var workspaceConfig = new WorkspaceConfigEditor(harness.Monitor, WorkspaceCraftPath);
        var hostWorkspacePath = Path.GetDirectoryName(WorkspaceCraftPath)!;
        var mcpConfig = new AppServerMcpConfigService(harness.Monitor, null, hostWorkspacePath, WorkspaceCraftPath, null);
        var runtimeConfig = new AppServerRuntimeConfigRefresher(harness.Service, harness.Monitor, WorkspaceCraftPath, workspaceConfig);
        return new AppServerManagedPluginRefresher(
            new SkillsLoader(WorkspaceCraftPath),
            harness.Monitor,
            WorkspaceCraftPath,
            hostWorkspacePath,
            builtInRoots,
            mcpConfig,
            lspServerManager: null,
            contextPageManager: null,
            workspaceConfig,
            runtimeConfig,
            hookRunner: null,
            managementState,
            logger: null);
    }

    private static async Task<JsonDocument> ListPluginsAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            Contract.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));
        var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response;
    }

    private static JsonElement FindPlugin(JsonDocument list, string id) =>
        list.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray()
            .First(plugin => string.Equals(plugin.GetProperty("id").GetString(), id, StringComparison.Ordinal));

    private static JsonElement FindInstalledPlugin(JsonDocument list, string id) =>
        list.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray()
            .Single(plugin => string.Equals(plugin.GetProperty("id").GetString(), id, StringComparison.Ordinal)
                && plugin.GetProperty("installed").GetBoolean());

    private void WriteBuiltInPlugin(string id, string content) =>
        WritePlugin(Path.Combine(Sources, id), id, content);

    private void WriteUserOwnedPlugin(string id, string content) =>
        WritePlugin(Path.Combine(Installed, id), id, content);

    private static void WritePlugin(string pluginRoot, string id, string content)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), $$"""
            {
              "schemaVersion": 1,
              "id": "{{id}}",
              "version": "1.0.0",
              "displayName": "{{content}}",
              "description": "Managed plugin fixture.",
              "interface": { "displayName": "{{content}}" }
            }
            """);
        File.WriteAllText(Path.Combine(pluginRoot, "content.txt"), content);
    }

    private static byte[] CreateRegistryArchive(string marketplaceName, string pluginId, string content)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(archive, $"repo/.craft/plugins/marketplace.json", $$"""
                {
                  "name": "{{marketplaceName}}",
                  "plugins": [
                    {
                      "name": "{{pluginId}}",
                      "source": { "source": "local", "path": "./plugins/{{pluginId}}" },
                      "policy": { "installation": "AVAILABLE", "authentication": "ON_INSTALL" },
                      "category": "Testing"
                    }
                  ]
                }
                """);
            WriteZipEntry(archive, $"repo/plugins/{pluginId}/.craft-plugin/plugin.json", $$"""
                {
                  "schemaVersion": 1,
                  "id": "{{pluginId}}",
                  "version": "1.0.0",
                  "displayName": "{{content}}",
                  "description": "Registry plugin fixture.",
                  "interface": { "displayName": "{{content}}" }
                }
                """);
            WriteZipEntry(archive, $"repo/plugins/{pluginId}/content.txt", content);
        }

        return stream.ToArray();
    }

    private static void WriteZipEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(text);
    }

    private sealed class StubHandler(HttpStatusCode statusCode, byte[]? body = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode);
            if (body != null)
                response.Content = new ByteArrayContent(body);
            return Task.FromResult(response);
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
