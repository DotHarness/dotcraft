using DotCraft.Workspaces;
using DotCraft.Configuration;
using DotCraft.Lsp;
using Xunit;

namespace DotCraft.Tests.Lsp;

public class LspServerManagerRoutingTests
{
    [Fact]
    public async Task InitializeAsync_BuildsExtensionRoutingMap()
    {
        var workspace = CreateWorkspace();
        var config = new AppConfig
        {
            Tools = new AppConfig.ToolsConfig
            {
                Lsp = new AppConfig.LspToolsConfig { Enabled = true }
            },
            LspServers =
            [
                new()
                {
                    Name = "csharp",
                    Enabled = true,
                    Command = "csharp-ls",
                    ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
                },
                new()
                {
                    Name = "typescript",
                    Enabled = true,
                    Command = "typescript-language-server",
                    ExtensionToLanguage = new Dictionary<string, string> { [".ts"] = "typescript" }
                }
            ]
        };

        await using var manager = new LspServerManager(
            config,
            new WorkspacePaths { WorkspacePath = workspace, CraftPath = Path.Combine(workspace, ".craft") });
        await manager.InitializeAsync();

        Assert.Equal(2, manager.GetAllServers().Count);
        Assert.Equal("csharp", manager.GetServerForFile(Path.Combine(workspace, "a.cs"))?.Name);
        Assert.Equal("typescript", manager.GetServerForFile(Path.Combine(workspace, "a.ts"))?.Name);
        Assert.Null(manager.GetServerForFile(Path.Combine(workspace, "a.py")));
    }

    [Fact]
    public async Task InitializeAsync_WhenLspToolDisabled_DoesNotLoadServers()
    {
        var workspace = CreateWorkspace();
        var config = new AppConfig
        {
            Tools = new AppConfig.ToolsConfig
            {
                Lsp = new AppConfig.LspToolsConfig { Enabled = false }
            },
            LspServers =
            [
                new()
                {
                    Name = "csharp",
                    Enabled = true,
                    Command = "csharp-ls",
                    ExtensionToLanguage = new Dictionary<string, string> { [".cs"] = "csharp" }
                }
            ]
        };

        await using var manager = new LspServerManager(
            config,
            new WorkspacePaths { WorkspacePath = workspace, CraftPath = Path.Combine(workspace, ".craft") });
        await manager.InitializeAsync();

        Assert.Empty(manager.GetAllServers());
    }

    [Fact]
    public async Task InitializeAsync_LoadsEffectiveWorkspaceAndPluginServers()
    {
        var workspace = CreateWorkspace();
        var craftPath = Path.Combine(workspace, ".craft");
        WriteLspPlugin(Path.Combine(craftPath, "plugins", "demo"), "demo-plugin");
        var config = new AppConfig
        {
            Tools = new AppConfig.ToolsConfig
            {
                Lsp = new AppConfig.LspToolsConfig { Enabled = true }
            },
            LspServers =
            [
                new()
                {
                    Name = "workspace-ts",
                    Enabled = true,
                    Command = "typescript-language-server",
                    ExtensionToLanguage = new Dictionary<string, string> { [".ts"] = "typescript" }
                }
            ]
        };

        await using var manager = new LspServerManager(
            config,
            new WorkspacePaths { WorkspacePath = workspace, CraftPath = craftPath });
        await manager.InitializeAsync();

        Assert.Equal(2, manager.GetAllServers().Count);
        Assert.Equal("workspace-ts", manager.GetServerForFile(Path.Combine(workspace, "a.ts"))?.Name);
        Assert.Equal("demo-plugin:csharp", manager.GetServerForFile(Path.Combine(workspace, "a.cs"))?.Name);
    }

    [Fact]
    public async Task InitializeAsync_WhenLspToolDisabled_DoesNotLoadPluginServers()
    {
        var workspace = CreateWorkspace();
        var craftPath = Path.Combine(workspace, ".craft");
        WriteLspPlugin(Path.Combine(craftPath, "plugins", "demo"), "demo-plugin");
        var config = new AppConfig
        {
            Tools = new AppConfig.ToolsConfig
            {
                Lsp = new AppConfig.LspToolsConfig { Enabled = false }
            }
        };

        await using var manager = new LspServerManager(
            config,
            new WorkspacePaths { WorkspacePath = workspace, CraftPath = craftPath });
        await manager.InitializeAsync();

        Assert.Empty(manager.GetAllServers());
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "dotcraft-lsp-routing-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static void WriteLspPlugin(string pluginRoot, string id)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["lsp"]
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".lsp.json"),
            """
{
  "lspServers": {
    "csharp": {
      "command": "csharp-ls",
      "extensionToLanguage": {
        ".cs": "csharp"
      }
    }
  }
}
""");
    }
}
