using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerPluginManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"plugin_management_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppServerPluginManagementTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Initialize_ReportsPluginManagementCapability()
    {
        using var harness = CreateHarness();
        using var init = await harness.InitializeAsync();

        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("pluginManagement")
            .GetBoolean());
    }

    [Fact]
    public async Task PluginList_ReturnsBrowserContents()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");
        Assert.Equal("browser", plugin.GetProperty("id").GetString());
        Assert.False(plugin.GetProperty("enabled").GetBoolean());
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());
        Assert.Equal("Browser", plugin.GetProperty("displayName").GetString());
        Assert.Empty(plugin.GetProperty("functions").EnumerateArray());
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "browser");
    }

    [Fact]
    public async Task PluginList_ReturnsInstallableDoctorSkillDisplayMetadata()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "dotcraft-doctor");
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());

        var skills = plugin.GetProperty("skills").EnumerateArray()
            .ToDictionary(item => item.GetProperty("name").GetString()!);
        Assert.Equal(3, skills.Count);
        Assert.Equal("Context Handoff", skills["context-handoff"].GetProperty("displayName").GetString());
        Assert.Equal(
            "Find failed sessions and export a clean Markdown handoff",
            skills["context-handoff"].GetProperty("shortDescription").GetString());
        Assert.Equal("Error Diagnosis", skills["error-diagnosis"].GetProperty("displayName").GetString());
        Assert.Equal(
            "Trace DotCraft failures through thread rollout and state DB evidence",
            skills["error-diagnosis"].GetProperty("shortDescription").GetString());
        Assert.Equal("Report Issue", skills["report-issue"].GetProperty("displayName").GetString());
        Assert.Equal(
            "Draft a public-safe GitHub issue from a diagnosis or bug report",
            skills["report-issue"].GetProperty("shortDescription").GetString());
    }

    [Fact]
    public async Task PluginList_WithoutBundledRootsDoesNotExposeUninstalledBuiltIns()
    {
        using var harness = CreateHarness(includeBundledRoots: false);
        await harness.InitializeAsync();

        var list = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.DoesNotContain(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");

        var install = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(install);

        using var installResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(installResponse, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task PluginList_ReturnsInstallableOratorioAppContents()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "oratorio");
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());

        var app = Assert.Single(plugin.GetProperty("apps").EnumerateArray());
        Assert.Equal("com.dotharness.oratorio", app.GetProperty("appId").GetString());
        Assert.Equal("Oratorio", app.GetProperty("displayName").GetString());
        Assert.Equal("oratorio", app.GetProperty("nativeApplication").GetProperty("protocol").GetString());
        Assert.Contains(
            app.GetProperty("toolCatalog").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "QueueReviewRound");

        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "oratorio");
    }

    [Fact]
    public async Task PluginList_ReturnsInstallableAgentTeamsMetadata()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == PluginIds.AgentTeams);
        Assert.Equal("Agent Teams", plugin.GetProperty("displayName").GetString());
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());
        Assert.Empty(plugin.GetProperty("apps").EnumerateArray());
        Assert.Empty(plugin.GetProperty("skills").EnumerateArray());
    }

    [Fact]
    public async Task PluginList_ReturnsDesktopExtensionDescriptors()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var teams = Assert.Single(plugins, item => item.GetProperty("id").GetString() == PluginIds.AgentTeams);
        var teamsExtension = Assert.Single(teams.GetProperty("desktopExtensions").EnumerateArray());
        Assert.Equal("team-card-board", teamsExtension.GetProperty("id").GetString());
        Assert.EndsWith("team-card-board.mjs", teamsExtension.GetProperty("entry").GetString(), StringComparison.Ordinal);
        Assert.Contains(
            teamsExtension.GetProperty("surfaces").EnumerateArray(),
            surface => surface.GetProperty("type").GetString() == "mainView"
                       && surface.GetProperty("viewId").GetString() == "teams");
    }

    [Fact]
    public async Task PluginList_ReturnsSkillOnlyPluginWithEmptyFunctions()
    {
        WriteSkillOnlyPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo-plugin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "demo-plugin");
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.Empty(plugin.GetProperty("functions").EnumerateArray());
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "demo-skill");
    }

    [Fact]
    public async Task PluginList_ReturnsWorkspaceMcpPlugin()
    {
        WriteMcpPlugin(Path.Combine(_workspaceCraftPath, "plugins", "review-tools"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "review-tools");
        Assert.Equal("Review Tools", plugin.GetProperty("displayName").GetString());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.False(plugin.GetProperty("installable").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());
        Assert.Equal("workspace", plugin.GetProperty("source").GetString());
        Assert.Empty(plugin.GetProperty("functions").EnumerateArray());
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "review-tools"
                    && item.GetProperty("enabled").GetBoolean());
        var mcpServer = Assert.Single(plugin.GetProperty("mcpServers").EnumerateArray());
        Assert.Equal("review", mcpServer.GetProperty("name").GetString());
        Assert.Equal("review-tools:review", mcpServer.GetProperty("runtimeName").GetString());
        Assert.Equal("stdio", mcpServer.GetProperty("transport").GetString());
        Assert.True(mcpServer.GetProperty("enabled").GetBoolean());
        Assert.True(mcpServer.GetProperty("active").GetBoolean());
    }

    [Fact]
    public async Task PluginList_WhenWorkspaceMcpShadowsPlugin_MarksPluginMcpShadowed()
    {
        WriteMcpPlugin(Path.Combine(_workspaceCraftPath, "plugins", "review-tools"));
        var config = new AppConfig();
        config.McpServers.Add(new McpServerConfig
        {
            Name = "review-tools:review",
            Enabled = false,
            Transport = "stdio",
            Command = "node"
        });
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "review-tools");
        var mcpServer = Assert.Single(plugin.GetProperty("mcpServers").EnumerateArray());
        Assert.False(mcpServer.GetProperty("active").GetBoolean());
        Assert.Equal("workspace", mcpServer.GetProperty("shadowedBy").GetString());
    }

    [Fact]
    public async Task PluginList_ReturnsWorkspaceLspPlugin()
    {
        WriteLspPlugin(Path.Combine(_workspaceCraftPath, "plugins", "csharp-lsp"));
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = true;
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "csharp-lsp");
        var lspServer = Assert.Single(plugin.GetProperty("lspServers").EnumerateArray());
        Assert.Equal("csharp", lspServer.GetProperty("name").GetString());
        Assert.Equal("csharp-lsp:csharp", lspServer.GetProperty("runtimeName").GetString());
        Assert.Equal("stdio", lspServer.GetProperty("transport").GetString());
        Assert.True(lspServer.GetProperty("enabled").GetBoolean());
        Assert.True(lspServer.GetProperty("active").GetBoolean());
        Assert.Contains(
            lspServer.GetProperty("extensions").EnumerateArray(),
            item => item.GetString() == ".cs");
    }

    [Fact]
    public async Task PluginList_WhenWorkspaceLspShadowsPlugin_MarksPluginLspShadowed()
    {
        WriteLspPlugin(Path.Combine(_workspaceCraftPath, "plugins", "csharp-lsp"));
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = true;
        config.LspServers.Add(new LspServerConfig
        {
            Name = "csharp-lsp:csharp",
            Enabled = true,
            Command = "custom-csharp-ls",
            ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
        });
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "csharp-lsp");
        var lspServer = Assert.Single(plugin.GetProperty("lspServers").EnumerateArray());
        Assert.False(lspServer.GetProperty("active").GetBoolean());
        Assert.Equal("workspace", lspServer.GetProperty("shadowedBy").GetString());
    }

    [Fact]
    public async Task McpList_ReturnsPluginOriginReadOnlyMetadata()
    {
        var manager = new McpClientManager();
        await manager.ConnectAsync([
            new McpServerConfig
            {
                Name = "review-tools:review",
                Enabled = false,
                Transport = "stdio",
                Command = "node",
                Origin = McpServerOrigin.Plugin("review-tools", "Review Tools", "review")
            }
        ]);
        using var harness = CreateHarness(mcpClientManager: manager);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.McpList, new { });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var server = Assert.Single(response.RootElement.GetProperty("result").GetProperty("servers").EnumerateArray());
        Assert.Equal("review-tools:review", server.GetProperty("name").GetString());
        Assert.True(server.GetProperty("readOnly").GetBoolean());
        var origin = server.GetProperty("origin");
        Assert.Equal("plugin", origin.GetProperty("kind").GetString());
        Assert.Equal("review-tools", origin.GetProperty("pluginId").GetString());
        Assert.Equal("Review Tools", origin.GetProperty("pluginDisplayName").GetString());
        Assert.Equal("review", origin.GetProperty("declaredName").GetString());
    }

    [Fact]
    public async Task McpRemove_WhenPluginOrigin_ReturnsReadOnlyError()
    {
        var manager = new McpClientManager();
        await manager.ConnectAsync([
            new McpServerConfig
            {
                Name = "review-tools:review",
                Enabled = false,
                Transport = "stdio",
                Command = "node",
                Origin = McpServerOrigin.Plugin("review-tools", "Review Tools", "review")
            }
        ]);
        using var harness = CreateHarness(mcpClientManager: manager);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.McpRemove, new { name = "review-tools:review" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.McpServerReadOnlyCode);
    }

    [Fact]
    public async Task PluginList_ReturnsDiagnosticsForInvalidLocalManifest()
    {
        WriteInvalidPlugin(Path.Combine(_workspaceCraftPath, "plugins", "broken-plugin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.DoesNotContain(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "broken-plugin");
        Assert.Contains(
            result.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "MissingPluginCapabilities"
                    && item.GetProperty("pluginId").GetString() == "broken-plugin");
    }

    [Fact]
    public async Task PluginInstall_DeploysBrowserAndEnablesContents()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = harness.BuildRequest(AppServerMethods.PluginInstallLocal, new { path = source });
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

        var msg = harness.BuildRequest(AppServerMethods.PluginInstallLocal, new { path = source });
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

            var msg = harness.BuildRequest(AppServerMethods.PluginInstallLocal, new { path = relativeSource });
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
    public async Task PluginInstall_DeploysOratorioAppAndSkill()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "oratorio" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("oratorio", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", "oratorio", ".builtin")));
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "oratorio");

        var app = Assert.Single(plugin.GetProperty("apps").EnumerateArray());
        Assert.Equal("com.dotharness.oratorio", app.GetProperty("appId").GetString());
        Assert.Equal("oratorio", app.GetProperty("toolNamespace").GetString());
    }

    [Fact]
    public async Task PluginInstall_DeploysAgentTeamsMetadataPlugin()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = PluginIds.AgentTeams });
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

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser" });
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

        var msg = harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new { toolsLspEnabled = true });
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

        var msg = harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = "browser", enabled = false });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("enabled").GetBoolean());
        var configJson = await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"));
        Assert.Contains("browser", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain("node-repl", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }

    [Fact]
    public async Task PluginSetEnabled_WhenNotInstalled_ReturnsError()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = "browser", enabled = true });
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

        var msg = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
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

        var msg = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "review-tools" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").TryGetProperty("plugin", out _));
        Assert.False(Directory.Exists(pluginRoot));

        var list = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
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

        var list = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var plugin = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "review-tools");
        Assert.Equal("explicit", plugin.GetProperty("source").GetString());
        Assert.False(plugin.GetProperty("removable").GetBoolean());

        var remove = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "review-tools" });
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

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.Equal("browser", plugin.GetProperty("id").GetString());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser");
    }

    private AppServerTestHarness CreateHarness(
        AppConfig? config = null,
        SkillsLoader? loader = null,
        McpClientManager? mcpClientManager = null,
        bool includeBundledRoots = true)
    {
        config ??= new AppConfig();
        loader ??= CreateSkillsLoader(config, includeBundledRoots);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            appConfigMonitor: new AppConfigMonitor(config),
            mcpClientManager: mcpClientManager,
            builtInPluginSourceRoots: includeBundledRoots ? [BundledPluginSourceRoot()] : []);
    }

    private SkillsLoader CreateSkillsLoader(AppConfig config, bool includeBundledRoots = true)
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        loader.SetDisabledSkills(config.Skills.DisabledSkills);
        PluginRuntimeConfigurator.ConfigureSkillsLoader(
            loader,
            config,
            _tempRoot,
            _workspaceCraftPath,
            builtInPluginSourceRoots: includeBundledRoots ? [BundledPluginSourceRoot()] : []);
        return loader;
    }

    private static async Task InstallBrowserAsync(AppServerTestHarness harness)
    {
        var install = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(install);
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
    }

    private static string BundledPluginSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "dotcraft.sln")))
                return Path.Combine(dir, "desktop", "resources", "plugins", "dotcraft-bundled", "plugins");
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static void WriteSkillOnlyPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo Plugin",
  "description": "Demo skill-only plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }

    private static void WriteMcpPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "review-tools"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "review-tools", "SKILL.md"),
            "---\nname: review-tools\ndescription: Review plugin skill\n---\n# Review");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".mcp.json"),
            """
{
  "mcpServers": {
    "review": {
      "transport": "stdio",
      "command": "node",
      "args": ["server.js"],
      "cwd": "./server"
    }
  }
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "review-tools",
  "version": "0.1.0",
  "displayName": "Review Tools",
  "description": "Review workflows and MCP tools.",
  "capabilities": ["skill", "mcp"],
  "skills": "./skills/",
  "mcpServers": "./.mcp.json",
  "interface": {
    "displayName": "Review Tools",
    "shortDescription": "Review workflows and MCP tools",
    "developerName": "DotHarness",
    "category": "Coding",
    "capabilities": ["Skill", "MCP"],
    "defaultPrompt": "Review this change.",
    "brandColor": "#2563EB"
  }
}
""");
    }

    private static void WriteLspPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".lsp.json"),
            """
{
  "lspServers": {
    "csharp": {
      "transport": "stdio",
      "command": "csharp-ls",
      "args": ["--stdio"],
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "csharp-lsp",
  "version": "0.1.0",
  "displayName": "C# LSP",
  "description": "C# language server plugin.",
  "capabilities": ["lsp"],
  "lspServers": "./.lsp.json"
}
""");
    }

    private static void WriteInvalidPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "broken-plugin",
  "version": "0.1.0",
  "displayName": "Broken Plugin",
  "description": "This manifest lacks supported contributions.",
  "capabilities": ["tool"]
}
""");
    }
}
