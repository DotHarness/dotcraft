using DotCraft.Configuration;
using DotCraft.Hooks;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginVariableExpansionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-variables-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LspLoader_ExpandsOriginalInputWithoutRewritingOpaqueValues()
    {
        var pluginRoot = Path.Combine(_root, "${PLUGIN_DATA}");
        var plugin = WritePlugin(pluginRoot);
        var dataPath = Path.Combine(_root, "${PLUGIN_ROOT}", "data");
        File.WriteAllText(Path.Combine(pluginRoot, ".lsp.json"), """
            {
              "lspServers": {
                "demo": {
                  "command": "node",
                  "arguments": ["${PLUGIN_ROOT}/../opaque|${PLUGIN_DATA}|${PLUGIN_ROOT}", "${UNSET_VALUE}"],
                  "environmentVariables": {
                    "VALUE": "${PLUGIN_DATA}/../opaque|${PLUGIN_ROOT}",
                    "${PLUGIN_ROOT}": "unchanged-key",
                    "plugin_root": "spoofed-root",
                    "plugin_data": "spoofed-data"
                  },
                  "extensionToLanguage": { ".demo": "demo" }
                }
              }
            }
            """);
        var diagnostics = new List<PluginDiagnostic>();

        var server = Assert.Single(PluginLspServerLoader.LoadPluginServers(plugin, diagnostics, dataPath));

        Assert.Empty(diagnostics);
        Assert.Equal(pluginRoot + "/../opaque|" + dataPath + "|" + pluginRoot, server.Arguments[0]);
        Assert.Equal("${UNSET_VALUE}", server.Arguments[1]);
        Assert.Equal(dataPath + "/../opaque|" + pluginRoot, server.EnvironmentVariables["VALUE"]);
        Assert.Equal("unchanged-key", server.EnvironmentVariables["${PLUGIN_ROOT}"]);
        Assert.Equal(pluginRoot, server.EnvironmentVariables["PLUGIN_ROOT"]);
        Assert.Equal(dataPath, server.EnvironmentVariables["PLUGIN_DATA"]);
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain("plugin_root", server.EnvironmentVariables.Keys);
            Assert.DoesNotContain("plugin_data", server.EnvironmentVariables.Keys);
        }
    }

    [Fact]
    public void HookLoader_ExpandsOnceAndProtectsHostPluginEnvironment()
    {
        var workspace = Path.Combine(_root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "${PLUGIN_DATA}");
        WritePlugin(pluginRoot);
        File.WriteAllText(Path.Combine(pluginRoot, "hooks.json"), """
            {
              "hooks": {
                "PreToolUse": [{
                  "hooks": [{
                    "type": "command",
                    "command": "echo ${PLUGIN_ROOT}/../opaque|${PLUGIN_DATA}|${UNSET_VALUE}",
                    "environmentVariables": {
                      "VALUE": "${PLUGIN_ROOT}|${PLUGIN_DATA}",
                      "PLUGIN_ROOT": "spoofed-root",
                      "PLUGIN_DATA": "spoofed-data",
                      "plugin_root": "spoofed-lower-root",
                      "plugin_data": "spoofed-lower-data"
                    }
                  }]
                }]
              }
            }
            """);
        var paths = new DotCraftPaths(workspace, botPath, Path.Combine(_root, "user-data"));
        var config = new AppConfig();
        var first = new HooksLoader(paths).Discover(config, workspace);
        var hook = Assert.Single(first.Hooks);
        config.Hooks.State[hook.Key] = new HookStateConfig { TrustedHash = hook.CurrentHash };

        var result = new HooksLoader(paths).Discover(config, workspace);
        var runtimeHook = Assert.Single(Assert.Single(result.RuntimeConfig.Hooks[nameof(HookEvent.PreToolUse)]).Hooks);
        var dataPath = PluginDataPaths.Resolve(paths, "demo-plugin");

        Assert.Equal("echo " + pluginRoot + "/../opaque|" + dataPath + "|${UNSET_VALUE}", runtimeHook.Command);
        Assert.Equal(pluginRoot + "|" + dataPath, runtimeHook.EnvironmentVariables["VALUE"]);
        Assert.Equal(pluginRoot, runtimeHook.EnvironmentVariables["PLUGIN_ROOT"]);
        Assert.Equal(dataPath, runtimeHook.EnvironmentVariables["PLUGIN_DATA"]);
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain("plugin_root", runtimeHook.EnvironmentVariables.Keys);
            Assert.DoesNotContain("plugin_data", runtimeHook.EnvironmentVariables.Keys);
        }
        Assert.Equal(OperatingSystem.IsWindows() ? 3 : 5, runtimeHook.EnvironmentVariables.Count);
    }

    [Fact]
    public void HookLoader_LeavesWorkspaceHookPlaceholdersLiteral()
    {
        var workspace = Path.Combine(_root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        Directory.CreateDirectory(botPath);
        File.WriteAllText(Path.Combine(botPath, "hooks.json"), """
            {
              "hooks": {
                "PreToolUse": [{
                  "hooks": [{
                    "type": "command",
                    "command": "echo ${PLUGIN_ROOT}|${PLUGIN_DATA}",
                    "environmentVariables": { "VALUE": "${PLUGIN_ROOT}|${PLUGIN_DATA}" }
                  }]
                }]
              }
            }
            """);
        var paths = new DotCraftPaths(workspace, botPath, Path.Combine(_root, "user-data"));
        var config = new AppConfig();
        var hook = Assert.Single(new HooksLoader(paths).Discover(config, workspace).Hooks);
        config.Hooks.State[hook.Key] = new HookStateConfig { TrustedHash = hook.CurrentHash };

        var result = new HooksLoader(paths).Discover(config, workspace);
        var runtimeHook = Assert.Single(Assert.Single(result.RuntimeConfig.Hooks[nameof(HookEvent.PreToolUse)]).Hooks);

        Assert.Equal("echo ${PLUGIN_ROOT}|${PLUGIN_DATA}", runtimeHook.Command);
        Assert.Equal("${PLUGIN_ROOT}|${PLUGIN_DATA}", runtimeHook.EnvironmentVariables["VALUE"]);
        Assert.Single(runtimeHook.EnvironmentVariables);
    }

    private static DiscoveredPlugin WritePlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"), """
            {
              "schemaVersion": 1,
              "id": "demo-plugin",
              "version": "1.0.0",
              "displayName": "Demo",
              "description": "Variable expansion fixture.",
              "capabilities": ["hooks", "lsp"]
            }
            """);
        var manifest = new PluginManifest
        {
            SchemaVersion = 1,
            Id = "demo-plugin",
            DisplayName = "Demo",
            RootPath = pluginRoot,
            ManifestPath = Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            LspServersPath = Path.Combine(pluginRoot, ".lsp.json")
        };
        return new DiscoveredPlugin(manifest, PluginDiscoverySourceKind.Workspace, pluginRoot, true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
