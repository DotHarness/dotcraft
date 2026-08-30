using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Skills;
using DotCraft.AppServer;
using DotCraft.Workspaces;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;
using Xunit;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed partial class AppServerPluginManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"plugin_management_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;
    private readonly string _bundledPluginSourceRoot;

    public AppServerPluginManagementTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
        _bundledPluginSourceRoot = Path.Combine(_tempRoot, "bundled-plugins");
        WriteBundledPluginFixtures(_bundledPluginSourceRoot);
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

    private AppServerTestHarness CreateHarness(
        AppConfig? config = null,
        SkillsLoader? loader = null,
        McpClientManager? mcpClientManager = null,
        bool includeBundledRoots = true,
        IPluginDotnetRuntimeCoordinator? pluginDotnetRuntimeCoordinator = null)
    {
        config ??= new AppConfig();
        loader ??= CreateSkillsLoader(config, includeBundledRoots);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            appConfigMonitor: new AppConfigMonitor(config),
            mcpClientManager: mcpClientManager,
            pluginDotnetRuntimeCoordinator: pluginDotnetRuntimeCoordinator,
            pluginConfigStore: new PluginConfigStore(new DotCraftPaths(
                _tempRoot,
                _workspaceCraftPath,
                Path.Combine(_tempRoot, "user-data"))),
            builtInPluginSourceRoots: includeBundledRoots ? [_bundledPluginSourceRoot] : []);
    }

    private sealed class FakePluginRuntimeCoordinator(PluginRuntimeSnapshot snapshot)
        : IPluginDotnetRuntimeCoordinator
    {
        public PluginRuntimeSnapshot Snapshot { get; private set; } = snapshot;

        public event EventHandler<PluginRuntimeSnapshotChangedEventArgs>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken cancellationToken = default)
        {
            _ = pluginId;
            _ = enabled;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<PluginRuntimeMutationResult> QuiesceForMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(PluginRuntimeMutationOutcome.Applied, pluginId));

        public Task<PluginRuntimeMutationResult> ReconcileAfterMutationAsync(
            string pluginId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result(PluginRuntimeMutationOutcome.Applied, pluginId));

        public Task<PluginRuntimeMutationResult> TrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var alreadyTrusted = Find(pluginId)?.TrustStatus == PluginDotnetTrustStatus.Trusted;
            if (!alreadyTrusted)
                Retrust(pluginId, PluginDotnetTrustStatus.Trusted);
            return Task.FromResult(Result(
                alreadyTrusted ? PluginRuntimeMutationOutcome.NoChange : PluginRuntimeMutationOutcome.Applied,
                pluginId));
        }

        public Task<PluginRuntimeMutationResult> RevokeTrustAsync(
            string pluginId,
            CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            var alreadyUntrusted = Find(pluginId)?.TrustStatus != PluginDotnetTrustStatus.Trusted;
            if (!alreadyUntrusted)
                Retrust(pluginId, PluginDotnetTrustStatus.Untrusted);
            return Task.FromResult(Result(
                alreadyUntrusted ? PluginRuntimeMutationOutcome.NoChange : PluginRuntimeMutationOutcome.Applied,
                pluginId));
        }

        /// <summary>Simulates the bundle bytes changing under an existing grant.</summary>
        public void MarkBundleModified(string pluginId) =>
            Retrust(pluginId, PluginDotnetTrustStatus.Modified);

        private PluginDotnetRuntimeInfo? Find(string pluginId) =>
            Snapshot.Plugins.FirstOrDefault(plugin => PluginIds.EqualsCanonical(plugin.PluginId, pluginId));

        private void Retrust(string pluginId, PluginDotnetTrustStatus trust)
        {
            var nextRevision = Snapshot.Revision + 1;
            Snapshot = new PluginRuntimeSnapshot(
                nextRevision,
                Snapshot.Plugins.Select(plugin => PluginIds.EqualsCanonical(plugin.PluginId, pluginId)
                    ? plugin with
                    {
                        TrustStatus = trust,
                        State = trust == PluginDotnetTrustStatus.Trusted
                            ? PluginDotnetRuntimeState.Active
                            : PluginDotnetRuntimeState.Blocked,
                        GenerationId = trust == PluginDotnetTrustStatus.Trusted
                            ? $"{pluginId}-g{nextRevision}"
                            : null,
                        Blockers = trust switch
                        {
                            PluginDotnetTrustStatus.Trusted => [],
                            PluginDotnetTrustStatus.Modified =>
                            [
                                new PluginRuntimeBlocker(
                                    PluginDotnetDiagnosticCodes.TrustModified,
                                    "The accepted bundle changed after trust was granted.",
                                    new Dictionary<string, System.Text.Json.JsonElement>())
                            ],
                            _ =>
                            [
                                new PluginRuntimeBlocker(
                                    PluginDotnetDiagnosticCodes.Untrusted,
                                    "The plugin has no trust grant.",
                                    new Dictionary<string, System.Text.Json.JsonElement>())
                            ]
                        }
                    }
                    : plugin).ToArray(),
                Snapshot.Diagnostics);
        }

        private PluginRuntimeMutationResult Result(PluginRuntimeMutationOutcome outcome, string pluginId) =>
            new(outcome, [pluginId], []);
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
            builtInPluginSourceRoots: includeBundledRoots ? [_bundledPluginSourceRoot] : []);
        return loader;
    }

    private static FakePluginRuntimeCoordinator CreateUntrustedBrowserRuntime() =>
        new(new PluginRuntimeSnapshot(
            3,
            [new PluginDotnetRuntimeInfo(
                "browser",
                "1.0.0",
                PluginDotnetRuntimeState.Blocked,
                null,
                [new PluginRuntimeBlocker(
                    PluginDotnetDiagnosticCodes.Untrusted,
                    "The plugin has no trust grant.",
                    new Dictionary<string, System.Text.Json.JsonElement>())],
                TrustStatus: PluginDotnetTrustStatus.Untrusted)],
            []));

    // Clones the result element, because the harness disposes each response document.
    private static async Task<System.Text.Json.JsonElement> SetTrustedAsync(
        AppServerTestHarness harness,
        bool trusted,
        bool expectNotification = true)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSetTrusted,
            new { id = "browser", trusted }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result").Clone();
        if (!expectNotification)
            return result;

        using var notification = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated,
            notification.RootElement.GetProperty("method").GetString());
        Assert.Contains(
            notification.RootElement.GetProperty("params").GetProperty("pluginIds").EnumerateArray(),
            id => id.GetString() == "browser");
        return result;
    }

    private static async Task<System.Text.Json.JsonElement> ReadPluginListResultAsync(AppServerTestHarness harness)
    {
        await harness.ExecuteRequestAsync(harness.BuildRequest(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginList,
            new { includeDisabled = true }));

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        return response.RootElement.GetProperty("result").Clone();
    }

    private static async Task<System.Text.Json.JsonElement> ReadBrowserRuntimeAsync(AppServerTestHarness harness)
    {
        var result = await ReadPluginListResultAsync(harness);
        var plugin = Assert.Single(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "browser");
        return plugin.GetProperty("dotnetRuntime");
    }

    private static async Task<long> ReadListRevisionAsync(AppServerTestHarness harness) =>
        (await ReadPluginListResultAsync(harness)).GetProperty("snapshotRevision").GetInt64();

    private static async Task InstallBrowserAsync(AppServerTestHarness harness)
    {
        var install = harness.BuildRequest(DotCraft.Protocol.AppServer.AppServerMethodNames.PluginInstall, new { id = "browser" });
        await harness.ExecuteRequestAsync(install);
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        using var snapshotUpdated = await harness.Transport.ReadNextSentAsync();
        Assert.Equal(
            DotCraft.Protocol.AppServer.AppServerMethodNames.PluginSnapshotUpdated,
            snapshotUpdated.RootElement.GetProperty("method").GetString());
    }

    private static void WriteBundledPluginFixtures(string root)
    {
        WriteBrowserFixture(Path.Combine(root, "browser"));
        WriteAgentTeamsFixture(Path.Combine(root, PluginIds.AgentTeams));
    }

    private static void WriteBrowserFixture(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        WriteSkillFixture(Path.Combine(pluginRoot, "skills"), "browser", "Browser", "Control a test browser");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "browser",
  "version": "1.0.0",
  "displayName": "Browser",
  "description": "Test browser plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }

    private static void WriteAgentTeamsFixture(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "desktop", "dist"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "desktop", "dist", "index.mjs"),
            "export function activate() { return {}; }");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "agent-teams",
  "version": "1.0.0",
  "displayName": "Agent Teams",
  "description": "Test agent teams plugin.",
  "capabilities": ["metadata", "desktop"],
  "desktop": {
    "description": "Adds the Team board to DotCraft Desktop.",
    "entry": "./desktop/dist/index.mjs"
  },
  "interface": {
    "displayName": "Agent Teams",
    "shortDescription": "Test agent teams",
    "developerName": "DotCraft",
    "category": "Testing",
    "capabilities": ["Team"]
  }
}
""");
    }

    private static void WriteSkillFixture(
        string skillsRoot,
        string name,
        string displayName,
        string shortDescription)
    {
        var skillRoot = Path.Combine(skillsRoot, name);
        Directory.CreateDirectory(Path.Combine(skillRoot, "agents"));
        File.WriteAllText(
            Path.Combine(skillRoot, "SKILL.md"),
            $"---\nname: {name}\ndescription: Test skill\n---\n# {displayName}");
        File.WriteAllText(
            Path.Combine(skillRoot, "agents", "openai.yaml"),
            $$"""
interface:
  display_name: "{{displayName}}"
  short_description: "{{shortDescription}}"
""");
    }

    private void ConfigureRegistryAppRegistry(AppConfig config, bool includeBrokenEntry = false)
    {
        var registryRoot = Path.Combine(_tempRoot, "registry");
        Directory.CreateDirectory(Path.Combine(registryRoot, ".craft", "plugins"));
        Directory.CreateDirectory(Path.Combine(registryRoot, "plugins"));
        WriteRegistryMarketplace(registryRoot, "registry-app", includeBrokenEntry);
        WriteRegistryAppPlugin(Path.Combine(registryRoot, "plugins", "registry-app"));
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });
    }

    private static void WriteRegistryMarketplace(string registryRoot, string pluginId, bool includeBrokenEntry = false)
    {
        var brokenEntry = includeBrokenEntry
            ? """
,
    {
      "name": "broken-registry-entry",
      "source": {
        "source": "local",
        "path": "./../broken-registry-entry"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      },
      "category": "Testing"
    }
"""
            : string.Empty;
        File.WriteAllText(
            Path.Combine(registryRoot, ".craft", "plugins", "marketplace.json"),
            $$"""
{
  "name": "test-registry",
  "interface": {
    "displayName": "Test Registry"
  },
  "plugins": [
    {
      "name": "{{pluginId}}",
      "source": {
        "source": "local",
        "path": "./plugins/{{pluginId}}"
      },
      "policy": {
        "installation": "AVAILABLE",
        "authentication": "ON_INSTALL"
      },
      "category": "Productivity"
    }
    {{brokenEntry}}
  ]
}
""");
    }

    private static void WriteRegistryAppPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "registry-app"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "registry-app", "SKILL.md"),
            "---\nname: registry-app\ndescription: Registry App\n---\n# Registry App");
        File.WriteAllText(
            Path.Combine(pluginRoot, "apps.json"),
            """
{
  "apps": [
    {
      "appId": "com.example.registry-app",
      "displayName": "Registry App",
      "developerName": "Example Labs",
      "description": "Manage registry app workflows from selected DotCraft threads.",
      "category": "Productivity",
      "nativeApplication": {
        "displayName": "Registry App",
        "protocol": "registryapp",
        "installUrl": "https://example.com/registry-app"
      },
      "connection": {
        "handoffModes": [
          {
            "mode": "customProtocol",
            "uriTemplate": "registryapp://dotcraft/{operation}?app={appId}&request={requestId}&token={requestToken}&endpoint={endpoint}"
          }
        ]
      }
    }
  ]
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "registry-app",
  "version": "0.1.0",
  "displayName": "Registry App",
  "description": "Manage registry app workflows from selected DotCraft threads.",
  "capabilities": ["skill", "app"],
  "skills": "./skills/",
  "apps": "./apps.json",
  "interface": {
    "displayName": "Registry App",
    "shortDescription": "Manage and inspect agent board",
    "developerName": "Example Labs",
    "category": "Productivity",
    "capabilities": ["App", "Skill"],
    "defaultPrompt": "Manage Registry App workflow tasks.",
    "brandColor": "#5B6FF0"
  }
}
""");
    }

    private static void WriteSkillOnlyPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill", "agents"));
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "shared.svg"), "<svg />");
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "agents", "openai.yaml"),
            """
interface:
  display_name: "Demo Skill"
  icon_small: "../../assets/shared.svg"
""");
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
      "arguments": ["server.js"],
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
    "developerName": "Example Labs",
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
      "arguments": ["--stdio"],
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

    private static void WriteDotNetPluginWithMissingEntry(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
            {
              "schemaVersion": 1,
              "id": "dotnet-demo",
              "version": "1.0.0",
              "displayName": "Dotnet Demo",
              "capabilities": ["dotnet"],
              "dotnet": {
                "minHostVersion": "0.1.0",
                "entryAssembly": "./dotnet/DotnetDemo.dll",
                "entryType": "DotnetDemo.Plugin",
                "exportedApiAssemblies": []
              },
              "dependencies": {
                "acme.core": "1.0.0"
              }
            }
            """);
    }
}
