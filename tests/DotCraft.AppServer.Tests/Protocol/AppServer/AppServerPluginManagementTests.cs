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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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
    public async Task PluginList_ProjectsRuntimeSnapshotAndNativeFunctions()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = new FakePluginRuntimeCoordinator(new PluginRuntimeSnapshot(
            7,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Active,
                "browser-g7",
                [],
                [new PluginRuntimeToolInfo("browser.search", "browser", "search", "Search the browser")])],
            []));
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(7, result.GetProperty("snapshotRevision").GetInt64());
        var plugin = Assert.Single(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");
        Assert.Equal("active", plugin.GetProperty("dotnetRuntime").GetProperty("state").GetString());
        Assert.Equal("browser-g7", plugin.GetProperty("dotnetRuntime").GetProperty("generationId").GetString());
        var function = Assert.Single(plugin.GetProperty("functions").EnumerateArray());
        Assert.Equal("browser", function.GetProperty("namespace").GetString());
        Assert.Equal("search", function.GetProperty("name").GetString());
    }

    [Fact]
    public async Task PluginList_CarriesTheRuntimesOwnDiagnostics()
    {
        WriteBrowserFixture(Path.Combine(_workspaceCraftPath, "plugins", "browser"));
        var runtime = new FakePluginRuntimeCoordinator(new PluginRuntimeSnapshot(
            3,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Faulted,
                null,
                [new PluginRuntimeBlocker(
                    "PluginActivationTimeout",
                    "Plugin activation did not complete before the deadline.",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal))],
                [])],
            [PluginDiagnostic.Error(
                "PluginActivationTimeout",
                "Plugin activation did not complete before the deadline.",
                "browser")]));
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var diagnostic = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "PluginActivationTimeout");
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("browser", diagnostic.GetProperty("pluginId").GetString());
    }


    [Fact]
    public async Task PluginList_ReturnsInstallableDoctorSkillDisplayMetadata()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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
        Assert.Equal(4, skills.Count);
        Assert.Equal("DotCraft Doctor", skills["dotcraft-doctor"].GetProperty("displayName").GetString());
        Assert.Equal(
            "Route diagnosis, context handoff, and issue reporting",
            skills["dotcraft-doctor"].GetProperty("shortDescription").GetString());
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

        var list = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.DoesNotContain(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");

        var install = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(install);

        using var installResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(installResponse, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task PluginList_ReturnsInstallableRegistryAppContents()
    {
        var config = new AppConfig();
        ConfigureRegistryAppRegistry(config);
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "registry-app");
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());

        var app = Assert.Single(plugin.GetProperty("apps").EnumerateArray());
        Assert.Equal("com.example.registry-app", app.GetProperty("appId").GetString());
        Assert.Equal("Registry App", app.GetProperty("displayName").GetString());
        Assert.Equal("registryapp", app.GetProperty("nativeApplication").GetProperty("protocol").GetString());
        Assert.False(app.TryGetProperty("toolNamespace", out _));
        Assert.False(app.TryGetProperty("toolCatalog", out _));

        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "registry-app");
    }

    [Fact]
    public async Task PluginList_ReturnsInstallableAgentTeamsMetadata()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpList, new { });
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
    public async Task McpServerStatusList_ReturnsCanonicalPaginatedRuntimeShape()
    {
        await using var manager = new McpClientManager();
        await manager.ConnectAsync([
            new McpServerConfig
            {
                Name = "binding:board",
                Enabled = false,
                Transport = "stdio",
                Command = "node",
                Origin = McpServerOrigin.Binding("board-binding", "board")
            },
            new McpServerConfig
            {
                Name = "workspace-tools",
                Enabled = false,
                Transport = "stdio",
                Command = "node"
            }
        ]);
        using var harness = CreateHarness(mcpClientManager: manager);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpServerStatusList, new { limit = 1 });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        var status = Assert.Single(result.GetProperty("data").EnumerateArray());
        Assert.Equal("binding:board", status.GetProperty("runtimeName").GetString());
        Assert.Equal("board", status.GetProperty("declaredName").GetString());
        Assert.Equal("binding", status.GetProperty("origin").GetProperty("kind").GetString());
        Assert.Equal("1", result.GetProperty("nextCursor").GetString());
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpRemove, new { name = "review-tools:review" });
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

        var msg = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList, new { includeDisabled = true });
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
    public async Task PluginList_ProjectsDotnetMetadataDependenciesAndStructuredDiagnostics()
    {
        WriteDotNetPluginWithMissingEntry(Path.Combine(_workspaceCraftPath, "plugins", "dotnet-demo"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(
            response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "dotnet-demo");
        Assert.Equal("./dotnet/DotnetDemo.dll", plugin.GetProperty("dotnet").GetProperty("entryAssembly").GetString());
        Assert.Equal("0.1.0", plugin.GetProperty("dotnet").GetProperty("minHostVersion").GetString());
        var dependency = Assert.Single(plugin.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("acme.core", dependency.GetProperty("id").GetString());
        Assert.Equal("1.0.0", dependency.GetProperty("requiredVersion").GetString());
        Assert.Equal("missing", dependency.GetProperty("availability").GetString());
        var diagnostic = Assert.Single(
            plugin.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "PluginEntryAssemblyMissing");
        Assert.Equal(
            "./dotnet/DotnetDemo.dll",
            diagnostic.GetProperty("parameters").GetProperty("assemblyPath").GetString());
    }

}
