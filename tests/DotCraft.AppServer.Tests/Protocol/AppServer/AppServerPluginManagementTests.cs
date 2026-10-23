using System.Diagnostics;
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
    public async Task PluginList_ReturnsBrowserContents()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        var plugin = FindPlugin(result, "browser");
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
    public async Task PluginList_WithManyDeclarativeRequirements_CompletesWithinFiveSeconds()
    {
        var sharedSkills = Path.Combine(_tempRoot, "shared-skills");
        for (var index = 0; index < 31; index++)
        {
            var skillDir = Path.Combine(sharedSkills, $"shared-{index:D2}");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(
                Path.Combine(skillDir, "SKILL.md"),
                $"---\nname: shared-{index:D2}\ndescription: Shared skill {index}\nbins: command-that-does-not-exist-dotcraft\n---\nShared instructions");
        }

        for (var index = 0; index < 17; index++)
        {
            WriteSkillPlugin(
                Path.Combine(_workspaceCraftPath, "plugins", $"plugin-{index:D2}"),
                $"plugin-{index:D2}",
                $"plugin-skill-{index:D2}");
        }

        var loader = new SkillsLoader(_workspaceCraftPath, sharedSkillsPath: sharedSkills);
        using var harness = CreateHarness(loader: loader, includeBundledRoots: false);
        await harness.InitializeAsync();
        var stopwatch = Stopwatch.StartNew();

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));

        stopwatch.Stop();
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"plugin/list took {stopwatch.Elapsed}.");
        Assert.Equal(17, response.RootElement.GetProperty("result").GetProperty("plugins").GetArrayLength());
    }

    [Fact]
    public async Task PluginList_ProjectsRuntimeSnapshotNativeFunctionsAndDiagnostics()
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
            [PluginDiagnostic.Error(
                "PluginActivationTimeout",
                "Plugin activation did not complete before the deadline.",
                "browser")]));
        using var harness = CreateHarness(pluginDotnetRuntimeCoordinator: runtime);
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        Assert.Equal(7, result.GetProperty("snapshotRevision").GetInt64());
        var plugin = FindPlugin(result, "browser");
        Assert.Equal("active", plugin.GetProperty("dotnetRuntime").GetProperty("state").GetString());
        Assert.Equal("browser-g7", plugin.GetProperty("dotnetRuntime").GetProperty("generationId").GetString());
        var function = Assert.Single(plugin.GetProperty("functions").EnumerateArray());
        Assert.Equal("browser", function.GetProperty("namespace").GetString());
        Assert.Equal("search", function.GetProperty("name").GetString());
        var diagnostic = Assert.Single(
            result.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "PluginActivationTimeout");
        Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
        Assert.Equal("browser", diagnostic.GetProperty("pluginId").GetString());
    }

    [Fact]
    public async Task PluginList_WithoutBundledRootsDoesNotExposeUninstalledBuiltIns()
    {
        using var harness = CreateHarness(includeBundledRoots: false);
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        Assert.DoesNotContain(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");

        var install = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(install);

        using var installResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(installResponse, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task RegistryApp_IsListedAndItsSkillReadableWithoutInstalling()
    {
        var config = new AppConfig();
        ConfigureRegistryAppRegistry(config);
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var plugin = FindPlugin(await ReadPluginListResultAsync(harness), "registry-app");
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

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSkillRead,
            new { id = "registry-app", name = "registry-app" }));
        using (var response = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(response);
            var result = response.RootElement.GetProperty("result");
            Assert.Equal("registry-app", result.GetProperty("id").GetString());
            Assert.Equal("registry-app", result.GetProperty("name").GetString());
            Assert.Contains("# Registry App", result.GetProperty("content").GetString());
        }

        foreach (var (id, name) in new[]
                 {
                     ("", "registry-app"),
                     ("registry-app", ""),
                     ("missing-plugin", "registry-app"),
                     ("registry-app", "missing-skill")
                 })
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(
                DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSkillRead,
                new { id, name }));
            using var rejected = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsErrorResponse(rejected, AppServerErrors.InvalidParamsCode);
        }

        Assert.False(FindPlugin(await ReadPluginListResultAsync(harness), "registry-app").GetProperty("installed").GetBoolean());
    }

    [Fact]
    public async Task SkillOnlyPlugin_IsListedWithSharedIconsInListViewAndSkills()
    {
        WriteSkillOnlyPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo-plugin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var listed = FindPlugin(await ReadPluginListResultAsync(harness), "demo-plugin");
        Assert.True(listed.GetProperty("enabled").GetBoolean());
        Assert.True(listed.GetProperty("installed").GetBoolean());
        Assert.Empty(listed.GetProperty("functions").EnumerateArray());
        AssertSharedIcon(Assert.Single(listed.GetProperty("skills").EnumerateArray()));

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginView, new { id = "demo-plugin" }));
        using (var view = await harness.Transport.ReadNextSentAsync())
        {
            AppServerTestHarness.AssertIsSuccessResponse(view);
            AssertSharedIcon(Assert.Single(view.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("skills").EnumerateArray()));
        }

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.SkillsList, new { }));
        using var skills = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(skills);
        var skill = Assert.Single(
            skills.RootElement.GetProperty("result").GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "demo-skill");
        Assert.Equal("plugin", skill.GetProperty("source").GetString());
        Assert.Equal("demo-plugin", skill.GetProperty("pluginId").GetString());
        Assert.Equal("data:image/svg+xml;base64,PHN2ZyAvPg==", skill.GetProperty("iconSmallDataUrl").GetString());

        static void AssertSharedIcon(JsonElement skill)
        {
            Assert.Equal("data:image/svg+xml;base64,PHN2ZyAvPg==", skill.GetProperty("iconSmallDataUrl").GetString());
            Assert.Equal(skill.GetProperty("iconSmallDataUrl").GetString(), skill.GetProperty("iconLargeDataUrl").GetString());
        }
    }

    [Fact]
    public async Task PluginList_ReturnsWorkspaceMcpAndLspPlugins()
    {
        WriteMcpPlugin(Path.Combine(_workspaceCraftPath, "plugins", "review-tools"));
        WriteLspPlugin(Path.Combine(_workspaceCraftPath, "plugins", "csharp-lsp"));
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = true;
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        var plugin = FindPlugin(result, "review-tools");
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

        var lspServer = Assert.Single(FindPlugin(result, "csharp-lsp").GetProperty("lspServers").EnumerateArray());
        Assert.Equal("csharp", lspServer.GetProperty("name").GetString());
        Assert.Equal("csharp-lsp:csharp", lspServer.GetProperty("runtimeName").GetString());
        Assert.Equal("stdio", lspServer.GetProperty("transport").GetString());
        Assert.True(lspServer.GetProperty("enabled").GetBoolean());
        Assert.True(lspServer.GetProperty("active").GetBoolean());
        Assert.Contains(lspServer.GetProperty("extensions").EnumerateArray(), item => item.GetString() == ".cs");
    }

    [Fact]
    public async Task PluginList_WhenWorkspaceServersShadowPluginServers_MarksThemShadowed()
    {
        WriteMcpPlugin(Path.Combine(_workspaceCraftPath, "plugins", "review-tools"));
        WriteLspPlugin(Path.Combine(_workspaceCraftPath, "plugins", "csharp-lsp"));
        var config = new AppConfig();
        config.Tools.Lsp.Enabled = true;
        config.McpServers.Add(new McpServerConfig
        {
            Name = "review-tools:review",
            Enabled = false,
            Transport = "stdio",
            Command = "node"
        });
        config.LspServers.Add(new LspServerConfig
        {
            Name = "csharp-lsp:csharp",
            Enabled = true,
            Command = "custom-csharp-ls",
            ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
        });
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        var mcpServer = Assert.Single(FindPlugin(result, "review-tools").GetProperty("mcpServers").EnumerateArray());
        Assert.False(mcpServer.GetProperty("active").GetBoolean());
        Assert.Equal("workspace", mcpServer.GetProperty("shadowedBy").GetString());
        var lspServer = Assert.Single(FindPlugin(result, "csharp-lsp").GetProperty("lspServers").EnumerateArray());
        Assert.False(lspServer.GetProperty("active").GetBoolean());
        Assert.Equal("workspace", lspServer.GetProperty("shadowedBy").GetString());
    }

    [Fact]
    public async Task PluginOriginMcpServer_IsListedReadOnlyAndCannotBeRemoved()
    {
        await using var manager = new McpClientManager();
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

        await harness.ExecuteRequestAsync(harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.McpList, new { }));
        using (var response = await harness.Transport.ReadNextSentAsync())
        {
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

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.McpRemove,
            new { name = "review-tools:review", scope = "workspace" }));
        using var removeResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(removeResponse, AppServerErrors.McpServerReadOnlyCode);
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
    public async Task PluginList_ReportsManifestAndDotnetDiagnostics()
    {
        WriteInvalidPlugin(Path.Combine(_workspaceCraftPath, "plugins", "broken-plugin"));
        WriteDotNetPluginWithMissingEntry(Path.Combine(_workspaceCraftPath, "plugins", "dotnet-demo"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var result = await ReadPluginListResultAsync(harness);
        Assert.DoesNotContain(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "broken-plugin");
        Assert.Contains(
            result.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "MissingPluginCapabilities"
                    && item.GetProperty("pluginId").GetString() == "broken-plugin");

        var plugin = FindPlugin(result, "dotnet-demo");
        Assert.Equal("Review integration", plugin.GetProperty("dotnet").GetProperty("displayName").GetString());
        Assert.Equal("Provides native review capabilities.", plugin.GetProperty("dotnet").GetProperty("description").GetString());
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

    private static JsonElement FindPlugin(JsonElement listResult, string id) =>
        Assert.Single(
            listResult.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == id);
}
