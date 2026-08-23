using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Skills;
using DotCraft.AppServer;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task PluginSetTrusted_GrantActivatesBlockedPluginAndRevokeBlocksItAgain()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = CreateUntrustedBrowserRuntime();
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        var blocked = await ReadBrowserRuntimeAsync(harness);
        Assert.Equal("blocked", blocked.GetProperty("state").GetString());
        Assert.Equal("untrusted", blocked.GetProperty("trustStatus").GetString());
        Assert.Contains(
            blocked.GetProperty("blockers").EnumerateArray(),
            item => item.GetProperty("code").GetString() == PluginDotnetDiagnosticCodes.Untrusted);

        var granted = await SetTrustedAsync(harness, trusted: true);
        Assert.Equal("applied", granted.GetProperty("outcome").GetString());
        var activeRuntime = granted.GetProperty("plugin").GetProperty("dotnetRuntime");
        Assert.Equal("active", activeRuntime.GetProperty("state").GetString());
        Assert.Equal("trusted", activeRuntime.GetProperty("trustStatus").GetString());
        Assert.Empty(activeRuntime.GetProperty("blockers").EnumerateArray());

        var revoked = await SetTrustedAsync(harness, trusted: false);
        Assert.Equal("applied", revoked.GetProperty("outcome").GetString());
        var revokedRuntime = revoked.GetProperty("plugin").GetProperty("dotnetRuntime");
        Assert.Equal("blocked", revokedRuntime.GetProperty("state").GetString());
        Assert.Equal("untrusted", revokedRuntime.GetProperty("trustStatus").GetString());
    }

    [Fact]
    public async Task PluginSetTrusted_RepeatingCurrentTrustStateReturnsNoChange()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: CreateUntrustedBrowserRuntime());
        await harness.InitializeAsync();

        var result = await SetTrustedAsync(harness, trusted: false, expectNotification: false);

        Assert.Equal("noChange", result.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task PluginSetTrusted_AfterBundleBytesChange_ReprojectsModifiedUntilReconfirmed()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = CreateUntrustedBrowserRuntime();
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();
        await SetTrustedAsync(harness, trusted: true);

        // An update replaces the bundle bytes, so the fingerprint-bound grant no longer matches.
        runtime.MarkBundleModified("browser");

        var modified = await ReadBrowserRuntimeAsync(harness);
        Assert.Equal("modified", modified.GetProperty("trustStatus").GetString());
        Assert.Equal("blocked", modified.GetProperty("state").GetString());
        Assert.Contains(
            modified.GetProperty("blockers").EnumerateArray(),
            item => item.GetProperty("code").GetString() == PluginDotnetDiagnosticCodes.TrustModified);

        var reconfirmed = await SetTrustedAsync(harness, trusted: true);
        Assert.Equal("applied", reconfirmed.GetProperty("outcome").GetString());
        Assert.Equal(
            "trusted",
            reconfirmed.GetProperty("plugin").GetProperty("dotnetRuntime").GetProperty("trustStatus").GetString());
    }

    [Fact]
    public async Task PluginSnapshotRevision_IncreasesMonotonicallyAcrossMutations()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = CreateUntrustedBrowserRuntime();
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        var observed = new List<long> { await ReadListRevisionAsync(harness) };
        observed.Add((await SetTrustedAsync(harness, trusted: true)).GetProperty("snapshotRevision").GetInt64());
        observed.Add(await ReadListRevisionAsync(harness));
        observed.Add((await SetTrustedAsync(harness, trusted: false)).GetProperty("snapshotRevision").GetInt64());
        observed.Add(await ReadListRevisionAsync(harness));

        Assert.Equal(observed.Order().ToArray(), observed);
        Assert.True(observed[^1] > observed[0]);
    }
    [Fact]
    public async Task PluginInstall_DeploysBrowserAndEnablesContents()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("applied", response.RootElement.GetProperty("result").GetProperty("outcome").GetString());
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", "browser", ".builtin")));
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }

    [Fact]
    public async Task PluginInstallLocal_InstallsUserPluginAsRemovable()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var source = Path.Combine(_tempRoot, "source-plugin");
        WriteSkillOnlyPlugin(source);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal, new { path = source });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("demo-plugin", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());

        var installed = Path.Combine(_workspaceCraftPath, "plugins", "demo-plugin");
        Assert.True(File.Exists(Path.Combine(installed, ".craft-plugin", "plugin.json")));
        // Local installs are user-owned: no .builtin marker is written, yet the plugin is removable.
        Assert.False(File.Exists(Path.Combine(installed, ".builtin")));
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "demo-skill");
    }

    [Fact]
    public async Task PluginInstallLocal_RejectsNonPluginFolder()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var source = Path.Combine(_tempRoot, "not-a-plugin");
        Directory.CreateDirectory(source);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal, new { path = source });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.False(Directory.Exists(Path.Combine(_workspaceCraftPath, "plugins", "demo-plugin")));
    }

    [Fact]
    public async Task PluginInstallLocal_RejectsRelativePathWithoutWritingWorkspacePlugin()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var relativeSource = "dotcraft-relative-plugin-test-" + Guid.NewGuid().ToString("N");
        var source = Path.Combine(Directory.GetCurrentDirectory(), relativeSource);

        try
        {
            WriteSkillOnlyPlugin(source);

            var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal, new { path = relativeSource });
            await harness.ExecuteRequestAsync(msg);

            using var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
            Assert.False(Directory.Exists(Path.Combine(_workspaceCraftPath, "plugins")));
        }
        finally
        {
            if (Directory.Exists(source))
                Directory.Delete(source, recursive: true);
        }
    }

    [Fact]
    public async Task PluginInstall_DeploysRegistryAppAndSkill()
    {
        var config = new AppConfig();
        ConfigureRegistryAppRegistry(config);
        var loader = CreateSkillsLoader(config);
        using var harness = CreateHarness(config, loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "registry-app" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("registry-app", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", "registry-app", ".builtin")));
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "registry-app");

        var app = Assert.Single(plugin.GetProperty("apps").EnumerateArray());
        Assert.Equal("com.example.registry-app", app.GetProperty("appId").GetString());
        Assert.False(app.TryGetProperty("toolNamespace", out _));
    }

    [Fact]
    public async Task PluginInstall_DeploysRegistryPluginWhenUnrelatedRegistryEntryHasError()
    {
        var config = new AppConfig();
        ConfigureRegistryAppRegistry(config, includeBrokenEntry: true);
        var loader = CreateSkillsLoader(config);
        using var harness = CreateHarness(config, loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "registry-app" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("registry-app", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", "registry-app", ".builtin")));
    }

    [Fact]
    public async Task PluginInstall_DeploysAgentTeamsMetadataPlugin()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = PluginIds.AgentTeams });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal(PluginIds.AgentTeams, plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.False(plugin.GetProperty("installable").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", PluginIds.AgentTeams, ".builtin")));
    }

    [Fact]
    public async Task PluginInstall_EmitsLspConfigRegion()
    {
        var changes = new List<AppConfigChangedEventArgs>();
        using var harness = CreateHarness();
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var change = Assert.Single(changes);
        Assert.Contains(ConfigChangeRegions.Plugins, change.Regions);
        Assert.Contains(ConfigChangeRegions.Skills, change.Regions);
        Assert.Contains(ConfigChangeRegions.Mcp, change.Regions);
        Assert.Contains(ConfigChangeRegions.Lsp, change.Regions);

        harness.Monitor.Changed -= OnChanged;
        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_TogglesToolsLspEnabledAndEmitsLspRegion()
    {
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = false;
        var changes = new List<AppConfigChangedEventArgs>();
        using var harness = CreateHarness(config);
        harness.Monitor.Changed += OnChanged;
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.WorkspaceConfigUpdate, new { toolsLspEnabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(response.RootElement.GetProperty("result").GetProperty("toolsLspEnabled").GetBoolean());
        Assert.True(config.Tools.Lsp.Enabled);
        var change = Assert.Single(changes);
        Assert.Contains(ConfigChangeRegions.Lsp, change.Regions);
        var configJson = await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"));
        Assert.Contains("\"Tools\"", configJson, StringComparison.Ordinal);
        Assert.Contains("\"Lsp\"", configJson, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": true", configJson, StringComparison.Ordinal);

        harness.Monitor.Changed -= OnChanged;
        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task PluginSetEnabled_DisablesBrowserAndWritesCanonicalId()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);
        await InstallBrowserAsync(harness);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled, new { id = "browser", enabled = false });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("applied", response.RootElement.GetProperty("result").GetProperty("outcome").GetString());
        Assert.False(response.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("enabled").GetBoolean());
        var configJson = await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"));
        Assert.Contains("browser", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain("node-repl", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }

    [Fact]
    public async Task PluginSetEnabled_WhenAlreadyInRequestedState_ReturnsNoChangeAndWritesNothing()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);
        await InstallBrowserAsync(harness);

        var msg = harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            new { id = "browser", enabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("noChange", response.RootElement.GetProperty("result").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task PluginSetEnabled_WhenNotInstalled_ReturnsError()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled, new { id = "browser", enabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task PluginRemove_RemovesManagedBuiltInDirectory()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);
        await InstallBrowserAsync(harness);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("applied", response.RootElement.GetProperty("result").GetProperty("outcome").GetString());
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.False(plugin.GetProperty("enabled").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(_workspaceCraftPath, "plugins", "browser")));
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }

    [Fact]
    public async Task PluginRemove_RemovesWorkspaceLocalUserPluginDirectory()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "review-tools");
        WriteMcpPlugin(pluginRoot);
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove, new { id = "review-tools" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            response.RootElement.GetProperty("result").GetProperty("plugin").ValueKind);
        Assert.False(Directory.Exists(pluginRoot));

        using var snapshotUpdated = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated,
            snapshotUpdated.RootElement.GetProperty("method").GetString());

        var list = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.DoesNotContain(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "review-tools");
    }

    [Fact]
    public async Task PluginRemove_ExplicitPluginRootIsRejected()
    {
        var pluginRoot = Path.Combine(_tempRoot, "external-plugins", "review-tools");
        WriteMcpPlugin(pluginRoot);
        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(Path.Combine(_tempRoot, "external-plugins"));
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var list = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var plugin = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "review-tools");
        Assert.Equal("explicit", plugin.GetProperty("source").GetString());
        Assert.False(plugin.GetProperty("removable").GetBoolean());

        var remove = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove, new { id = "review-tools" });
        await harness.ExecuteRequestAsync(remove);

        using var removeResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(removeResponse, AppServerErrors.InvalidParamsCode);
        Assert.True(Directory.Exists(pluginRoot));
    }

    [Fact]
    public async Task PluginInstall_DoesNotTreatNodeReplDisabledAsBrowserDisabled()
    {
        var config = new AppConfig();
        config.Plugins.DisabledPlugins.Add("node-repl");
        var loader = CreateSkillsLoader(config);
        using var harness = CreateHarness(config, loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("browser", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }
}
