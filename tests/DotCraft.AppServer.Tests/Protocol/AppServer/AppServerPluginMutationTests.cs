using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.AppServer;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests
{
    [Fact]
    public async Task PluginSetTrusted_GrantsAndRevokesWithMonotonicRevisions()
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

        var observed = new List<long> { await ReadListRevisionAsync(harness) };
        var unchanged = await SetTrustedAsync(harness, trusted: false, expectNotification: false);
        Assert.Equal("noChange", unchanged.GetProperty("outcome").GetString());

        var granted = await SetTrustedAsync(harness, trusted: true);
        observed.Add(granted.GetProperty("snapshotRevision").GetInt64());
        observed.Add(await ReadListRevisionAsync(harness));
        Assert.Equal("applied", granted.GetProperty("outcome").GetString());
        var activeRuntime = granted.GetProperty("plugin").GetProperty("dotnetRuntime");
        Assert.Equal("active", activeRuntime.GetProperty("state").GetString());
        Assert.Equal("trusted", activeRuntime.GetProperty("trustStatus").GetString());
        Assert.Empty(activeRuntime.GetProperty("blockers").EnumerateArray());

        var revoked = await SetTrustedAsync(harness, trusted: false);
        observed.Add(revoked.GetProperty("snapshotRevision").GetInt64());
        observed.Add(await ReadListRevisionAsync(harness));
        Assert.Equal("applied", revoked.GetProperty("outcome").GetString());
        var revokedRuntime = revoked.GetProperty("plugin").GetProperty("dotnetRuntime");
        Assert.Equal("blocked", revokedRuntime.GetProperty("state").GetString());
        Assert.Equal("untrusted", revokedRuntime.GetProperty("trustStatus").GetString());

        Assert.Equal(observed.Order().ToArray(), observed);
        Assert.True(observed[^1] > observed[0]);
    }

    [Fact]
    public async Task PluginSetTrusted_KeepsTrustOnInvalidParamsAndRequiresReconfirmationAfterBundleChange()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = CreateUntrustedBrowserRuntime();
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();
        await SetTrustedAsync(harness, trusted: true);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
            new { id = "browser" }));
        using (var response = await harness.Transport.ReadNextSentAsync())
            AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        var stillTrusted = await ReadBrowserRuntimeAsync(harness);
        Assert.Equal("trusted", stillTrusted.GetProperty("trustStatus").GetString());
        Assert.Equal("active", stillTrusted.GetProperty("state").GetString());

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
    public async Task PluginInstall_DeploysBrowserEnablesContentsAndReportsChangedRegions()
    {
        var config = new AppConfig();
        config.Plugins.DisabledPlugins.Add("node-repl");
        var loader = CreateSkillsLoader(config);
        var changes = new List<AppConfigChangedEventArgs>();
        using var harness = CreateHarness(config, loader);
        harness.Monitor.Changed += OnChanged;
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
        Assert.Equal("userglobal", plugin.GetProperty("source").GetString());
        Assert.True(File.Exists(Path.Combine(_userDataPath, "plugins", "browser", ".builtin")));
        Assert.False(Directory.Exists(Path.Combine(_workspaceCraftPath, "plugins")));
        Assert.True(File.Exists(Path.Combine(_userDataPath, "imports", "revision.json")));
        Assert.Contains(loader.ListSkills(), skill => skill.Name == "browser");

        var change = Assert.Single(changes);
        Assert.Contains(ConfigChangeRegions.Plugins, change.Regions);
        Assert.Contains(ConfigChangeRegions.Skills, change.Regions);
        Assert.Contains(ConfigChangeRegions.Mcp, change.Regions);
        Assert.Contains(ConfigChangeRegions.Lsp, change.Regions);

        harness.Monitor.Changed -= OnChanged;
        void OnChanged(object? sender, AppConfigChangedEventArgs args) => changes.Add(args);
    }

    [Fact]
    public async Task PluginInstallLocal_RejectsInvalidSourcesAndInstallsUserPluginAsRemovable()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var notAPlugin = Path.Combine(_tempRoot, "not-a-plugin");
        Directory.CreateDirectory(notAPlugin);
        var relativeSource = "dotcraft-relative-plugin-test-" + Guid.NewGuid().ToString("N");
        var relativeRoot = Path.Combine(Directory.GetCurrentDirectory(), relativeSource);
        try
        {
            WriteSkillOnlyPlugin(relativeRoot);
            foreach (var path in new[] { notAPlugin, relativeSource })
            {
                await harness.ExecuteRequestAsync(harness.BuildRequest(
                    DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal,
                    new { path }));
                using var rejected = await harness.Transport.ReadNextSentAsync();
                AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.InvalidParamsCode);
            }
        }
        finally
        {
            if (Directory.Exists(relativeRoot))
                Directory.Delete(relativeRoot, recursive: true);
        }
        Assert.False(Directory.Exists(Path.Combine(_userDataPath, "plugins")));

        var source = Path.Combine(_tempRoot, "source-plugin");
        WriteSkillOnlyPlugin(source);
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstallLocal,
            new { path = source }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("demo-plugin", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());

        var installed = Path.Combine(_userDataPath, "plugins", "demo-plugin");
        Assert.True(File.Exists(Path.Combine(installed, ".craft-plugin", "plugin.json")));
        Assert.False(File.Exists(Path.Combine(installed, ".builtin")));
        Assert.Contains(loader.ListSkills(), skill => skill.Name == "demo-skill");
    }

    [Fact]
    public async Task PluginInstall_DeploysRegistryAppDespiteUnrelatedBrokenRegistryEntry()
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
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_userDataPath, "plugins", "registry-app", ".builtin")));
        Assert.Contains(loader.ListSkills(), skill => skill.Name == "registry-app");

        var app = Assert.Single(plugin.GetProperty("apps").EnumerateArray());
        Assert.Equal("com.example.registry-app", app.GetProperty("appId").GetString());
        Assert.False(app.TryGetProperty("toolNamespace", out _));
    }

    [Fact]
    public async Task PluginSetEnabled_DisablesUserGlobalPluginInWorkspaceLayerOnly()
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
        var userConfigPath = Path.Combine(_userDataPath, "config.json");
        Assert.False(File.Exists(userConfigPath) && File.ReadAllText(userConfigPath).Contains("DisabledPlugins", StringComparison.Ordinal));
        Assert.DoesNotContain(loader.ListSkills(), skill => skill.Name == "browser");
    }

    [Fact]
    public async Task PluginSetEnabled_RejectsInvalidRequestsAndIsIdempotent()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        foreach (var (method, parameters) in new (string Method, object Params)[]
                 {
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled, new { id = "browser", enabled = true }),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled, new { enabled = false }),
                     (DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted, new { trusted = false })
                 })
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(method, parameters));
            using var rejected = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.InvalidParamsCode);
        }

        await InstallBrowserAsync(harness);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            new { id = "browser" }));
        using (var missingEnabled = await harness.Transport.ReadNextSentAsync())
            AppServerTestHarness.AssertIsErrorResponse(missingEnabled, AppServerErrors.InvalidParamsCode);
        Assert.True(FindPlugin(await ReadPluginListResultAsync(harness), "browser").GetProperty("enabled").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetEnabled,
            new { id = "browser", enabled = true }));
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal("noChange", response.RootElement.GetProperty("result").GetProperty("outcome").GetString());
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
        Assert.False(Directory.Exists(Path.Combine(_userDataPath, "plugins", "browser")));
        Assert.DoesNotContain(loader.ListSkills(), skill => skill.Name == "browser");
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

        Assert.DoesNotContain(
            (await ReadPluginListResultAsync(harness)).GetProperty("plugins").EnumerateArray(),
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

        var plugin = FindPlugin(await ReadPluginListResultAsync(harness), "review-tools");
        Assert.Equal("explicit", plugin.GetProperty("source").GetString());
        Assert.False(plugin.GetProperty("removable").GetBoolean());

        var remove = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginRemove, new { id = "review-tools" });
        await harness.ExecuteRequestAsync(remove);

        using var removeResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(removeResponse, AppServerErrors.InvalidParamsCode);
        Assert.True(Directory.Exists(pluginRoot));
    }
}
