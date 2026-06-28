using DotCraft.Configuration;
using DotCraft.Lsp;
using DotCraft.Plugins;
using System.IO.Compression;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void ManifestParser_AcceptsInterfaceAndSkillsPath()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"), "---\nname: demo-skill\n---\n# Demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "icon.svg"), "<svg />");
        WriteSkillOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            extra: """
,
  "interface": {
    "displayName": "Demo Plugin",
    "shortDescription": "Demo short.",
    "longDescription": "Demo long.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Read"],
    "defaultPrompt": "Try demo",
    "brandColor": "#123456",
    "composerIcon": "./assets/icon.svg",
    "logo": "./assets/icon.svg"
  }
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest!.SkillsPath!));
        Assert.Equal("Demo Plugin", result.Manifest.Interface?.DisplayName);
        Assert.Equal(Path.Combine(pluginRoot, "assets", "icon.svg"), result.Manifest.Interface?.ComposerIcon);
    }

    [Fact]
    public void ManifestParser_AcceptsSkillOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteSkillOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest!.SkillsPath!));
    }

    [Fact]
    public void ManifestParser_AcceptsMcpOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteMcpOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, ".mcp.json"), result.Manifest!.McpServersPath);
    }

    [Fact]
    public void ManifestParser_AcceptsLspOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteLspOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, ".lsp.json"), result.Manifest!.LspServersPath);
    }

    [Fact]
    public void ManifestParser_AcceptsExplicitLspServersPath()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteLspOnlyPlugin(pluginRoot, id: "demo-plugin", explicitPath: true);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, "lsp", "servers.json"), result.Manifest!.LspServersPath);
    }

    [Fact]
    public void ManifestParser_AcceptsInterfaceOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteInterfaceOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal("Demo Plugin", result.Manifest!.Interface?.DisplayName);
    }

    [Fact]
    public void ManifestParser_AcceptsDesktopExtensionOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteDesktopExtensionOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, "desktop-extensions.json"), result.Manifest!.DesktopExtensionsPath);
    }

    [Fact]
    public void PluginDesktopExtensionCatalog_CoalescesNullDescriptorCollections()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteDesktopExtensionOnlyPlugin(pluginRoot, id: "demo-plugin");
        File.WriteAllText(
            Path.Combine(pluginRoot, "desktop-extensions.json"),
            """
{
  "extensions": [
    {
      "id": "valid-view",
      "displayName": "Valid view",
      "entry": "./desktop/demo.mjs",
      "styles": null,
      "surfaces": [
        { "type": "mainView", "viewId": "valid", "label": "Valid" }
      ],
      "requiredAppIds": null,
      "connectOrigins": null
    },
    {
      "id": "missing-surfaces",
      "displayName": "Missing surfaces",
      "entry": "./desktop/demo.mjs",
      "styles": null,
      "surfaces": null
    }
  ]
}
""");
        var parse = PluginManifestParser.Load(pluginRoot);
        Assert.NotNull(parse.Manifest);
        var plugin = new DiscoveredPlugin(
            parse.Manifest!,
            PluginDiscoverySourceKind.Workspace,
            pluginRoot,
            Enabled: true);
        var diagnostics = new List<PluginDiagnostic>();

        var extensions = PluginDesktopExtensionCatalog.LoadPluginDesktopExtensions(plugin, diagnostics);

        var extension = Assert.Single(extensions);
        Assert.Equal("valid-view", extension.Id);
        Assert.Empty(extension.Styles);
        Assert.Empty(extension.RequiredAppIds);
        Assert.Empty(extension.ConnectOrigins);
        Assert.Contains(diagnostics, d => d.Code == "MissingDesktopExtensionSurfaces");
    }

    [Fact]
    public void ManifestParser_IgnoresLegacyNativeToolFieldsWhenSupportedCapabilityExists()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteSkillOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            extra: """
,
  "tools": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Legacy tool.",
      "inputSchema": { "type": "object" },
      "backend": { "kind": "process", "processId": "demo" }
    }
  ],
  "processes": {
    "demo": {
      "command": "python",
      "args": ["./tools/demo_tool.py"]
    }
  }
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedPluginNativeTools");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
    }

    [Fact]
    public void ManifestParser_RejectsLegacyNativeToolOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["tool"],
  "functions": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Legacy function.",
      "inputSchema": { "type": "object" },
      "backend": { "kind": "builtin", "providerId": "demo", "functionName": "EchoText" }
    }
  ]
}
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedPluginNativeTools");
        Assert.Contains(result.Diagnostics, d => d.Code == "MissingPluginCapabilities");
    }

    [Fact]
    public void PluginMcpServerLoader_LoadsEnabledPluginServersWithPluginRootCwd()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteMcpOnlyPlugin(pluginRoot, id: "demo-plugin");
        var config = new AppConfig();

        var servers = PluginMcpServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal("demo-plugin:review", server.Name);
        Assert.Equal("stdio", server.Transport);
        Assert.Equal("node", server.Command);
        Assert.Equal(["server.js"], server.Arguments);
        Assert.Equal(Path.Combine(pluginRoot, "server"), server.Cwd);
    }

    [Fact]
    public void PluginLspServerLoader_LoadsEnabledPluginServersWithOrigin()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteLspOnlyPlugin(pluginRoot, id: "demo-plugin");
        var config = new AppConfig();

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal("demo-plugin:csharp", server.Name);
        Assert.Equal("stdio", server.Transport);
        Assert.Equal("csharp-ls", server.Command);
        Assert.Equal(["--stdio"], server.Arguments);
        Assert.Equal("csharp", server.ExtensionToLanguage[".cs"]);
        Assert.True(server.ReadOnly);
        Assert.Equal("plugin", server.Origin.Kind);
        Assert.Equal("demo-plugin", server.Origin.PluginId);
        Assert.Equal("csharp", server.Origin.DeclaredName);
        Assert.Equal(pluginRoot, server.EnvironmentVariables["DOTCRAFT_PLUGIN_ROOT"]);
    }

    [Fact]
    public void PluginLspServerLoader_ExpandsPluginVariables()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteLspOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            lspJson:
            """
{
  "lspServers": {
    "csharp": {
      "transport": "stdio",
      "command": "${DOTCRAFT_PLUGIN_ROOT}/server/bin/csharp-ls",
      "args": ["--cache", "${DOTCRAFT_PLUGIN_DATA}/cache"],
      "env": {
        "PLUGIN_HOME": "${DOTCRAFT_PLUGIN_ROOT}",
        "PLUGIN_CACHE": "${DOTCRAFT_PLUGIN_DATA}/cache"
      },
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
        var config = new AppConfig();

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal(Path.Combine(pluginRoot, "server", "bin", "csharp-ls"), server.Command);
        Assert.Equal(Path.Combine(server.EnvironmentVariables["DOTCRAFT_PLUGIN_DATA"], "cache"), server.Arguments[1]);
        Assert.Equal(pluginRoot, server.EnvironmentVariables["PLUGIN_HOME"]);
        Assert.Equal(
            Path.Combine(server.EnvironmentVariables["DOTCRAFT_PLUGIN_DATA"], "cache"),
            server.EnvironmentVariables["PLUGIN_CACHE"]);
    }

    [Fact]
    public void PluginLspServerLoader_ResolvesPluginRelativeCommand()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteLspOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            lspJson:
            """
{
  "lspServers": {
    "csharp": {
      "transport": "stdio",
      "command": "./server/bin/csharp-ls",
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
        var config = new AppConfig();

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal(Path.Combine(pluginRoot, "server", "bin", "csharp-ls"), server.Command);
    }

    [Fact]
    public void PluginLspServerLoader_RejectsEscapingPluginRelativeCommand()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteLspOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            lspJson:
            """
{
  "lspServers": {
    "csharp": {
      "transport": "stdio",
      "command": "./../outside/csharp-ls",
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
        var config = new AppConfig();

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.Empty(servers);
        Assert.Contains(diagnostics, d => d.Code == "InvalidPluginLspServer");
    }

    [Fact]
    public void PluginLspServerLoader_ProbesWindowsExecutableSuffixForPluginRelativeCommand()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "server", "bin"));
        File.WriteAllText(Path.Combine(pluginRoot, "server", "bin", "csharp-ls.exe"), "");
        WriteLspOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            lspJson:
            """
{
  "lspServers": {
    "csharp": {
      "transport": "stdio",
      "command": "./server/bin/csharp-ls",
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
        var config = new AppConfig();

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal(Path.Combine(pluginRoot, "server", "bin", "csharp-ls.exe"), server.Command);
    }

    [Theory]
    [InlineData("csharp-lsp")]
    [InlineData("cpp-lsp")]
    public void SampleLspPlugins_AreSkillAndLspPlugins(string sampleName)
    {
        var root = FindRepositoryRoot();
        var pluginRoot = Path.Combine(root, "samples", "plugins", sampleName);

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, ".lsp.json"), result.Manifest!.LspServersPath);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest.SkillsPath!));
    }

    [Theory]
    [InlineData("csharp-lsp", "server", "csharp-ls", "csharp-ls")]
    [InlineData("cpp-lsp", "server", "clangd", "bin", "clangd")]
    public void SampleLspPlugins_LoadWithoutBundledServerBinary(string sampleName, params string[] commandSegments)
    {
        var root = FindRepositoryRoot();
        var pluginRoot = Path.Combine(root, "samples", "plugins", sampleName);
        var workspace = Path.Combine(NewTempDir(), "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(pluginRoot);

        var servers = PluginLspServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal(Path.Combine(new[] { pluginRoot }.Concat(commandSegments).ToArray()), server.Command);
    }

    [Fact]
    public void PluginLspServerResolver_WorkspaceServerShadowsPluginRuntimeName()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var pluginServer = new LspServerConfig
        {
            Name = "demo-plugin:csharp",
            Command = "csharp-ls",
            ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" },
            Origin = LspServerOrigin.Plugin("demo-plugin", "Demo", "csharp")
        };
        var workspaceServer = new LspServerConfig
        {
            Name = "demo-plugin:csharp",
            Command = "custom-csharp-ls",
            ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
        };

        var effective = PluginLspServerResolver.BuildEffectiveServers([workspaceServer], [pluginServer]);
        var summaries = PluginLspServerResolver.BuildPluginLspServerSummaries(
            [
                new DiscoveredPlugin(
                    new PluginManifest
                    {
                        SchemaVersion = 1,
                        Id = "demo-plugin",
                        DisplayName = "Demo",
                        RootPath = NewTempDir(),
                        ManifestPath = Path.Combine(NewTempDir(), ".craft-plugin", "plugin.json")
                    },
                    PluginDiscoverySourceKind.Workspace,
                    NewTempDir(),
                    Enabled: true)
            ],
            [workspaceServer],
            diagnostics,
            pluginServersByPluginId: new Dictionary<string, IReadOnlyList<LspServerConfig>>
            {
                ["demo-plugin"] = [pluginServer]
            });

        var server = Assert.Single(effective);
        Assert.Equal("custom-csharp-ls", server.Command);
        var summary = Assert.Single(summaries["demo-plugin"]);
        Assert.False(summary.Active);
        Assert.Equal("workspace", summary.ShadowedBy);
    }

    [Fact]
    public void ManifestParser_RejectsManifestWithoutSupportedCapabilities()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["test"]
}
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "MissingPluginCapabilities");
    }

    [Fact]
    public void ManifestParser_RejectsEscapingSkillsPath()
    {
        var root = NewTempDir();
        WriteSkillOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "skills": "../skills"
""",
            includeSkillsField: false);

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsEscapingLspServersPath()
    {
        var root = NewTempDir();
        WriteLspOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "lspServers": "../.lsp.json"
""",
            includeLspServersField: false);

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsPathEscape()
    {
        var root = NewTempDir();
        WriteInterfaceOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "paths": {
    "asset": "./../secret.txt"
  }
""");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsAnyParentPathSegment()
    {
        var root = NewTempDir();
        WriteInterfaceOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "paths": {
    "asset": "./assets/../asset.txt"
  }
""");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void BuiltInPluginDeployer_DeploysBrowserManifest()
    {
        var root = NewTempDir();
        var deployer = new BuiltInPluginDeployer(root, [BundledPluginSourceRoot()]);

        var diagnostics = deployer.Deploy();

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(root, "browser", ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(root, "browser", ".builtin")));
        Assert.True(File.Exists(Path.Combine(root, "browser", "skills", "browser", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(root, "chrome", ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(root, "chrome", ".builtin")));
        Assert.True(File.Exists(Path.Combine(root, "chrome", "skills", "chrome", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(root, "chrome", "scripts", "extension-id.json")));
        Assert.True(File.Exists(Path.Combine(root, "chrome", "extension", "manifest.json")));
        Assert.True(File.Exists(Path.Combine(root, "agent-teams", ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(root, "agent-teams", ".builtin")));
        Assert.True(File.Exists(Path.Combine(root, "agent-teams", "assets", "agent-teams.svg")));
    }

    [Fact]
    public void BuiltInPluginCatalog_WithoutConfiguredRootReturnsNoInstallableBuiltIns()
    {
        var result = new BuiltInPluginCatalog([]).Discover();

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void BuiltInPluginCatalog_DiscoversBundledManifestsFromFilesystemRoot()
    {
        var result = new BuiltInPluginCatalog([BundledPluginSourceRoot()]).Discover();

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var browser = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "browser");
        Assert.Equal(PluginDiscoverySourceKind.BuiltIn, browser.SourceKind);
        Assert.False(browser.Installed);
        Assert.True(browser.Installable);
        Assert.Equal("Browser", browser.Manifest.DisplayName);

        var agentTeams = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == PluginIds.AgentTeams);
        Assert.Equal(PluginDiscoverySourceKind.BuiltIn, agentTeams.SourceKind);
        Assert.False(agentTeams.Installed);
        Assert.True(agentTeams.Installable);
        Assert.Equal("Agent Teams", agentTeams.Manifest.DisplayName);
        Assert.Equal("Agent Teams", agentTeams.Manifest.Interface?.DisplayName);
    }

    [Fact]
    public void BuiltInPluginCatalog_DiscoversDotCraftDoctorPlugin()
    {
        var result = new BuiltInPluginCatalog([BundledPluginSourceRoot()]).Discover();

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var doctor = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "dotcraft-doctor");
        Assert.Equal(PluginDiscoverySourceKind.BuiltIn, doctor.SourceKind);
        Assert.False(doctor.Installed);
        Assert.True(doctor.Installable);
        Assert.Equal("DotCraft Doctor", doctor.Manifest.DisplayName);
        Assert.Equal("DotCraft Doctor", doctor.Manifest.Interface?.DisplayName);
        Assert.NotNull(doctor.Manifest.SkillsPath);

        // Both bundled skills must be present on disk so the plugin's right-click
        // actions (diagnose via error-diagnosis, draft a report via report-issue) work.
        var pluginDir = Path.Combine(BundledPluginSourceRoot(), "dotcraft-doctor");
        Assert.True(File.Exists(Path.Combine(pluginDir, "skills", "error-diagnosis", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(pluginDir, "skills", "report-issue", "SKILL.md")));
    }

    [Fact]
    public void BuiltInPluginCatalog_DiscoversSourceRegistryPluginFromLocalDirectory()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "registry-demo");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "registry-demo"), id: "registry-demo", displayName: "Registry Demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("registry-demo", plugin.Manifest.Id);
        Assert.Equal("Registry Demo", plugin.Manifest.DisplayName);
        Assert.False(plugin.Installed);
        Assert.True(plugin.Installable);
    }

    [Fact]
    public void BuiltInPluginCatalog_DiscoversSourceRegistryPluginFromArchiveSnapshot()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "registry-archive");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "registry-archive"), id: "registry-archive", displayName: "Registry Archive");
        var zipPath = Path.Combine(root, "registry.zip");
        ZipFile.CreateFromDirectory(registryRoot, zipPath);
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = zipPath });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("registry-archive", plugin.Manifest.Id);
        Assert.Equal("Registry Archive", plugin.Manifest.DisplayName);
    }

    [Fact]
    public void BuiltInPluginCatalog_DisableDefaultRegistryIgnoresHostDefaultUrl()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "default-demo");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "default-demo"), id: "default-demo");
        const string defaultRegistryEnv = "DOTCRAFT_DEFAULT_PLUGIN_REGISTRY_URL";
        var previous = Environment.GetEnvironmentVariable(defaultRegistryEnv);
        try
        {
            Environment.SetEnvironmentVariable(defaultRegistryEnv, registryRoot);
            var config = new AppConfig();
            config.Plugins.DisableDefaultPluginRegistry = true;

            var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

            Assert.Empty(result.Plugins);
        }
        finally
        {
            Environment.SetEnvironmentVariable(defaultRegistryEnv, previous);
        }
    }

    [Fact]
    public void BuiltInPluginCatalog_BundledPluginSuppressesRegistryDuplicate()
    {
        var root = NewTempDir();
        var bundledRoot = Path.Combine(root, "bundled");
        var registryRoot = Path.Combine(root, "registry");
        WriteSkillOnlyPlugin(Path.Combine(bundledRoot, "demo"), id: "demo", displayName: "Bundled Demo");
        WriteRegistryMarketplace(registryRoot, "demo");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "demo"), id: "demo", displayName: "Registry Demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([bundledRoot], config.Plugins).Discover();

        var plugin = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "demo");
        Assert.Equal("Bundled Demo", plugin.Manifest.DisplayName);
        Assert.Contains(result.Diagnostics, d => d.Code == "DuplicateBuiltInPluginId");
    }

    [Fact]
    public void BuiltInPluginCatalog_RejectsRegistrySourcePathEscape()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "escape-demo", sourcePath: "./../escape-demo");
        WriteSkillOnlyPlugin(Path.Combine(root, "escape-demo"), id: "escape-demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginRegistryEntryPath");
    }

    [Fact]
    public void BuiltInPluginCatalog_RejectsUnsupportedRegistrySourceKind()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "source-demo", sourceKind: "git-subdir");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "source-demo"), id: "source-demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginRegistryEntrySource");
    }

    [Fact]
    public void BuiltInPluginCatalog_RejectsRegistryManifestIdMismatch()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "marketplace-demo");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "marketplace-demo"), id: "manifest-demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, d => d.Code == "PluginRegistryManifestIdMismatch");
    }

    [Fact]
    public void BuiltInPluginCatalog_RequiresExplicitAvailableRegistryInstallationPolicy()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        WriteRegistryMarketplace(registryRoot, "policy-demo", policyInstallation: null);
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "policy-demo"), id: "policy-demo");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, d => d.Code == "PluginRegistryEntryNotAvailable" && d.PluginId == "policy-demo");
    }

    [Fact]
    public void BuiltInPluginCatalog_RejectsPlainHttpRegistryArchiveUrl()
    {
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
        {
            Url = "http://example.test/registry.zip"
        });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginRegistrySourceUrl");
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "PluginRegistryDownloadFailed");
    }

    [Fact]
    public void BuiltInPluginCatalog_AcceptsHttpsRegistryArchiveUrlAsRemoteSource()
    {
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig
        {
            Url = "https://127.0.0.1:9/registry.zip"
        });

        var result = new BuiltInPluginCatalog([], config.Plugins).Discover();

        Assert.Empty(result.Plugins);
        Assert.DoesNotContain(result.Diagnostics, d => d.Code == "InvalidPluginRegistrySourceUrl");
        Assert.Contains(result.Diagnostics, d => d.Code == "PluginRegistryDownloadFailed");
    }

    [Fact]
    public void BuiltInPluginDeployer_DeploysSourceRegistryPlugin()
    {
        var root = NewTempDir();
        var registryRoot = Path.Combine(root, "registry");
        var workspacePluginsRoot = Path.Combine(root, "workspace", ".craft", "plugins");
        WriteRegistryMarketplace(registryRoot, "registry-install");
        WriteSkillOnlyPlugin(Path.Combine(registryRoot, "plugins", "registry-install"), id: "registry-install");
        var config = new AppConfig();
        config.Plugins.PluginRegistries.Add(new AppConfig.PluginRegistryConfig { Url = registryRoot });

        var diagnostics = new BuiltInPluginDeployer(workspacePluginsRoot, [], config.Plugins)
            .DeployPlugin("registry-install");

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(workspacePluginsRoot, "registry-install", ".craft-plugin", "plugin.json")));
        Assert.StartsWith(
            "filesystem;sha256:",
            File.ReadAllText(Path.Combine(workspacePluginsRoot, "registry-install", BuiltInPluginDeployer.MarkerFile)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInPluginDeployer_MarkerIncludesResourceFingerprint()
    {
        var root = NewTempDir();

        new BuiltInPluginDeployer(root, [BundledPluginSourceRoot()]).Deploy();

        var marker = File.ReadAllText(Path.Combine(root, "browser", ".builtin"));
        Assert.StartsWith("filesystem;sha256:", marker, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovery_RefreshesInstalledManagedBuiltInPlugins()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "chrome");
        WriteInterfaceOnlyPlugin(pluginRoot, id: "chrome", displayName: "Stale Chrome");
        File.WriteAllText(Path.Combine(pluginRoot, BuiltInPluginDeployer.MarkerFile), "0.0.0.0");

        var result = new PluginDiscoveryService(Path.Combine(root, "global"), [BundledPluginSourceRoot()])
            .DiscoverAll(new AppConfig(), workspace, botPath);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(pluginRoot, "scripts", "extension-id.json")));
        Assert.StartsWith(
            "filesystem;sha256:",
            File.ReadAllText(Path.Combine(pluginRoot, BuiltInPluginDeployer.MarkerFile)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuiltInPluginDeployer_DoesNotOverwriteUserOwnedPlugin()
    {
        var root = NewTempDir();
        var userPlugin = Path.Combine(root, "browser");
        Directory.CreateDirectory(userPlugin);
        File.WriteAllText(Path.Combine(userPlugin, "owned.txt"), "mine");

        var diagnostics = new BuiltInPluginDeployer(root, [BundledPluginSourceRoot()]).Deploy();

        Assert.True(File.Exists(Path.Combine(userPlugin, "owned.txt")));
        Assert.False(File.Exists(Path.Combine(userPlugin, ".builtin")));
        Assert.Contains(diagnostics, d => d.Code == "BuiltInPluginUserOwned");
    }

    [Fact]
    public void Discovery_IgnoresRemovedBrowserUsePluginId()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var oldPluginRoot = Path.Combine(botPath, "plugins", "browser-use");
        WriteSkillOnlyPlugin(oldPluginRoot, id: "browser-use");
        File.WriteAllText(Path.Combine(oldPluginRoot, BuiltInPluginDeployer.MarkerFile), "old");

        var result = new PluginDiscoveryService(builtInPluginSourceRoots: [BundledPluginSourceRoot()])
            .DiscoverAll(new AppConfig(), workspace, botPath);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Plugins, plugin => plugin.Manifest.Id == "browser-use");
        Assert.Contains(result.Diagnostics, d => d.Code == "RemovedPluginIgnored" && d.PluginId == "browser-use");
    }

    [Fact]
    public void Discovery_UsesWorkspaceThenExplicitThenGlobalPrecedence()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var explicitRoot = Path.Combine(root, "explicit");
        var globalRoot = Path.Combine(root, "global");
        WriteInterfaceOnlyPlugin(Path.Combine(globalRoot, "demo"), id: "demo", displayName: "Global");
        WriteInterfaceOnlyPlugin(Path.Combine(explicitRoot, "demo"), id: "demo", displayName: "Explicit");
        WriteInterfaceOnlyPlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo", displayName: "Workspace");
        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(explicitRoot);

        var result = new PluginDiscoveryService(globalRoot).Discover(config, workspace, botPath);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("Workspace", plugin.Manifest.DisplayName);
        Assert.Contains(result.Diagnostics, d => d.Code == "DuplicatePluginId");
    }

    [Fact]
    public void Discovery_WorkspacePluginSuppressesBundledCatalogEntryWithSameId()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var bundledRoot = Path.Combine(root, "bundled", "plugins");
        WriteInterfaceOnlyPlugin(Path.Combine(bundledRoot, "demo"), id: "demo", displayName: "Bundled");
        WriteInterfaceOnlyPlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo", displayName: "Workspace");

        var result = new PluginDiscoveryService(Path.Combine(root, "global"), [bundledRoot])
            .DiscoverAll(new AppConfig(), workspace, botPath);

        var plugin = Assert.Single(result.Plugins, plugin => plugin.Manifest.Id == "demo");
        Assert.Equal("Workspace", plugin.Manifest.DisplayName);
        Assert.True(plugin.Installed);
        Assert.False(plugin.Installable);
    }

    [Fact]
    public void Discovery_LocalPluginsAreEnabledByDefaultAndDisabledPluginsOverride()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        WriteInterfaceOnlyPlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo");
        var config = new AppConfig();

        var enabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);
        config.Plugins.DisabledPlugins.Add("demo");
        var disabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);

        Assert.Single(enabled.Plugins);
        Assert.Empty(disabled.Plugins);
        Assert.Contains(disabled.Diagnostics, d => d.Code == "PluginDisabled");
    }

    [Fact]
    public void ConflictResolver_RejectsDuplicateFunctionNames()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var invoker = new NoopPluginInvoker();
        var registrations = new[]
        {
            new PluginFunctionRegistration(Descriptor("plugin-a", "SameName"), invoker),
            new PluginFunctionRegistration(Descriptor("plugin-b", "SameName"), invoker)
        };

        var resolved = PluginFunctionConflictResolver.ResolveRegistrations(registrations, diagnostics);

        Assert.Single(resolved);
        Assert.Contains(diagnostics, d => d.Code == "DuplicatePluginFunctionName");
    }

    private static PluginFunctionDescriptor Descriptor(string pluginId, string name) =>
        new()
        {
            PluginId = pluginId,
            Name = name,
            Description = name,
            InputSchema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" }
        };

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteSkillOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo",
        string extra = "",
        bool includeSkillsField = true)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        var skills = includeSkillsField ? """
,
  "skills": "./skills/"
""" : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["skill"]{{skills}}{{extra}}
}
""");
    }

    private static void WriteRegistryMarketplace(
        string registryRoot,
        string pluginId,
        string? sourcePath = null,
        string sourceKind = "local",
        string? policyInstallation = "AVAILABLE")
    {
        Directory.CreateDirectory(Path.Combine(registryRoot, ".craft", "plugins"));
        var policy = policyInstallation == null
            ? """
,
      "policy": {
        "authentication": "ON_INSTALL"
      }
"""
            : $$"""
,
      "policy": {
        "installation": "{{policyInstallation}}",
        "authentication": "ON_INSTALL"
      }
""";
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
        "source": "{{sourceKind}}",
        "path": "{{sourcePath ?? "./plugins/" + pluginId}}"
      }{{policy}},
      "category": "Testing"
    }
  ]
}
""");
    }

    private static void WriteMcpOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["mcp"]
}
""");
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
    }

    private static void WriteLspOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo",
        string extra = "",
        bool includeLspServersField = false,
        bool explicitPath = false,
        string? lspJson = null)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var lspServers = includeLspServersField || explicitPath
            ? explicitPath
                ? ",\n  \"lspServers\": \"./lsp/servers.json\""
                : ",\n  \"lspServers\": \"./.lsp.json\""
            : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["lsp"]{{lspServers}}{{extra}}
}
""");
        var lspPath = explicitPath ? Path.Combine(pluginRoot, "lsp", "servers.json") : Path.Combine(pluginRoot, ".lsp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lspPath)!);
        File.WriteAllText(
            lspPath,
            lspJson ??
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
    }

    private static string FindRepositoryRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            if (File.Exists(Path.Combine(dir, "dotcraft.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string BundledPluginSourceRoot() =>
        Path.Combine(FindRepositoryRoot(), "desktop", "resources", "plugins", "dotcraft-bundled", "plugins");

    private static void WriteInterfaceOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo",
        string extra = "")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["metadata"],
  "interface": {
    "displayName": "Demo Plugin",
    "shortDescription": "Demo short.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Metadata"],
    "defaultPrompt": "Try demo",
    "brandColor": "#2563EB"
  }{{extra}}
}
""");
    }

    private static void WriteDesktopExtensionOnlyPlugin(string pluginRoot, string id)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "desktop"));
        File.WriteAllText(Path.Combine(pluginRoot, "desktop", "demo.mjs"), "export default function Demo() {}");
        File.WriteAllText(
            Path.Combine(pluginRoot, "desktop-extensions.json"),
            """
{
  "extensions": [
    {
      "id": "demo-view",
      "displayName": "Demo view",
      "entry": "./desktop/demo.mjs",
      "surfaces": [
        { "type": "mainView", "viewId": "demo", "label": "Demo" }
      ]
    }
  ]
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Demo Plugin",
  "description": "Demo desktop extension plugin.",
  "capabilities": ["desktopExtension"],
  "desktopExtensions": "./desktop-extensions.json"
}
""");
    }

    private sealed class NoopPluginInvoker : IPluginFunctionInvoker
    {
        public ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginFunctionInvocationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PluginFunctionInvocationResult());
    }
}
