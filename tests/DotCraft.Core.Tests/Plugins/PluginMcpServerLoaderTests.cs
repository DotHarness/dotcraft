using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.McpTestServer;
using DotCraft.Plugins;
using DotCraft.Workspaces;
using Xunit;

namespace DotCraft.Tests.Plugins;

public sealed class PluginMcpServerLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dotcraft-mcp-plugin-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExpandsLaunchFieldsBeforeResolvingWorkingDirectory()
    {
        var plugin = WritePlugin(new
        {
            command = "${PLUGIN_ROOT}/bin/server",
            arguments = new[] { "${PLUGIN_ROOT}/server.js", "${PLUGIN_DATA}/../opaque", "${PLUGIN_ROOT}|${PLUGIN_DATA}|${PLUGIN_ROOT}" },
            environmentVariables = new Dictionary<string, string>
            {
                ["HOME_PATH"] = "${PLUGIN_ROOT}",
                ["CACHE_PATH"] = "${PLUGIN_DATA}/cache",
                ["${PLUGIN_ROOT}"] = "key stays literal"
            },
            cwd = "${PLUGIN_DATA}/cache/.."
        });
        var data = Path.Combine(_root, "user data", "plugin-data", "demo");
        var diagnostics = new List<PluginDiagnostic>();

        var server = Assert.Single(PluginMcpServerLoader.LoadPluginServers(plugin, diagnostics, data));

        Assert.Empty(diagnostics);
        Assert.Equal(Path.GetFullPath(Path.Combine(plugin.Manifest.RootPath, "bin/server")), server.Command);
        Assert.Equal(new[] { plugin.Manifest.RootPath + "/server.js", data + "/../opaque",
            plugin.Manifest.RootPath + "|" + data + "|" + plugin.Manifest.RootPath }, server.Arguments);
        Assert.Equal(data, server.Cwd);
        Assert.Equal(plugin.Manifest.RootPath, server.EnvironmentVariables["HOME_PATH"]);
        Assert.Equal(data + "/cache", server.EnvironmentVariables["CACHE_PATH"]);
        Assert.Equal("key stays literal", server.EnvironmentVariables["${PLUGIN_ROOT}"]);
        Assert.Equal(plugin.Manifest.RootPath, server.EnvironmentVariables["PLUGIN_ROOT"]);
        Assert.Equal(data, server.EnvironmentVariables["PLUGIN_DATA"]);
    }

    [Fact]
    public void ExpansionDoesNotRescanInsertedPathsOrExpandUnknownVariables()
    {
        var plugin = WritePlugin(new
        {
            command = "node",
            arguments = new[] { "${PLUGIN_ROOT}|${PLUGIN_DATA}", "${UNSET_VALUE}", "${plugin_root}" },
            environmentVariables = new { VALUE = "${PLUGIN_ROOT}|${PLUGIN_DATA}" }
        }, "plugin ${PLUGIN_DATA}");

        var server = Assert.Single(PluginMcpServerLoader.LoadPluginServers(plugin, [], Path.Combine(_root, "data")));

        Assert.Equal(plugin.Manifest.RootPath + "|" + Path.Combine(_root, "data"), server.Arguments[0]);
        Assert.Equal(server.Arguments[0], server.EnvironmentVariables["VALUE"]);
        Assert.Equal(new[] { "${UNSET_VALUE}", "${plugin_root}" }, server.Arguments.Skip(1));
        Assert.Null(server.Cwd);
    }

    [Fact]
    public void HostPluginEnvironmentCannotBeOverriddenOrForwarded()
    {
        var key = OperatingSystem.IsWindows() ? "plugin_root" : "PLUGIN_ROOT";
        var plugin = WritePlugin(new
        {
            command = "node",
            environmentVariables = new Dictionary<string, string> { [key] = "wrong root", ["PLUGIN_DATA"] = "wrong data" },
            envVars = new[] { key, "PLUGIN_ROOT", "PLUGIN_DATA", "PATH" }
        });
        var data = Path.Combine(_root, "data");

        var server = Assert.Single(PluginMcpServerLoader.LoadPluginServers(plugin, [], data));

        Assert.Equal(plugin.Manifest.RootPath, server.EnvironmentVariables["PLUGIN_ROOT"]);
        Assert.Equal(data, server.EnvironmentVariables["PLUGIN_DATA"]);
        Assert.Equal(new[] { "PATH" }, server.EnvVars);
        Assert.Equal(2, server.EnvironmentVariables.Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EffectiveRuntimeUsesSharedPluginDataRoot(bool configureUserData)
    {
        var workspace = Path.Combine(_root, "workspace");
        var bot = Path.Combine(workspace, ".craft");
        var user = configureUserData ? Path.Combine(_root, "user") : null;
        var paths = DotCraftPaths.CreateForExecutionHost(workspace, bot, user);
        WritePlugin(new { command = "node", arguments = new[] { "${PLUGIN_DATA}/cache" } },
            Path.Combine("workspace", ".craft", "plugins", "demo"));

        var servers = PluginMcpServerResolver.LoadEffectiveServers(new AppConfig(), paths, out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        var data = PluginDataPaths.Resolve(paths, "demo");
        Assert.Equal(data, server.EnvironmentVariables["PLUGIN_DATA"]);
        Assert.Equal(data + "/cache", Assert.Single(server.Arguments));
    }

    [Fact]
    public void HttpAndWorkspaceDeclarationsAreNotExpanded()
    {
        var plugin = WritePlugin(new
        {
            transport = "http",
            url = "https://example.test/${PLUGIN_ROOT}",
            headers = new { X_Plugin = "${PLUGIN_DATA}" }
        });
        var pluginServer = Assert.Single(PluginMcpServerLoader.LoadPluginServers(plugin, [], Path.Combine(_root, "data")));
        var workspaceServer = new McpServerConfig
        {
            Name = "workspace",
            Command = "${PLUGIN_ROOT}/server",
            Arguments = ["${PLUGIN_DATA}"]
        };

        var servers = PluginMcpServerResolver.BuildEffectiveServers([workspaceServer], [pluginServer]);

        Assert.Equal("https://example.test/${PLUGIN_ROOT}", servers[1].Url);
        Assert.Equal("${PLUGIN_DATA}", servers[1].Headers["X_Plugin"]);
        Assert.Empty(servers[1].EnvironmentVariables);
        Assert.Equal("${PLUGIN_ROOT}/server", servers[0].Command);
        Assert.Equal("${PLUGIN_DATA}", Assert.Single(servers[0].Arguments));
    }

    [Fact]
    public async Task ExpandedPluginArgumentStartsRealMcpProcessFromPathWithSpaces()
    {
        var plugin = WritePlugin(new
        {
            command = "dotnet",
            arguments = new[] { "${PLUGIN_ROOT}/DotCraft.McpTestServer.dll", "environment" },
            envVars = new[] { "plugin_root", "plugin_data" },
            cwd = "${PLUGIN_ROOT}"
        });
        var assembly = McpFixtureProcess.AssemblyPath;
        foreach (var suffix in new[] { ".dll", ".runtimeconfig.json", ".deps.json" })
        {
            var source = Path.ChangeExtension(assembly, null) + suffix;
            File.Copy(source, Path.Combine(plugin.Manifest.RootPath, Path.GetFileName(source)));
        }
        var server = Assert.Single(PluginMcpServerLoader.LoadPluginServers(plugin, [], Path.Combine(_root, "data")));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([server], timeout.Token);
        await manager.WaitForStartupCompletionAsync(timeout.Token);

        var status = Assert.Single(await manager.ListStatusesAsync(timeout.Token));
        Assert.True(status.StartupState == "ready", status.LastError);
        var result = await manager.CallToolAsync(server.Name, "environment",
            new Dictionary<string, object?> { ["names"] = new[] { "PLUGIN_ROOT", "PLUGIN_DATA", "plugin_root", "plugin_data" } },
            cancellationToken: timeout.Token);
        var values = JsonSerializer.SerializeToElement(result.StructuredContent);
        Assert.Equal(plugin.Manifest.RootPath, values.GetProperty("PLUGIN_ROOT").GetString());
        Assert.Equal(Path.Combine(_root, "data"), values.GetProperty("PLUGIN_DATA").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? plugin.Manifest.RootPath : Environment.GetEnvironmentVariable("plugin_root"),
            values.GetProperty("plugin_root").GetString());
        Assert.Equal(OperatingSystem.IsWindows() ? Path.Combine(_root, "data") : Environment.GetEnvironmentVariable("plugin_data"),
            values.GetProperty("plugin_data").GetString());
    }

    private DiscoveredPlugin WritePlugin(object server, string directory = "plugin with spaces")
    {
        var root = Path.Combine(_root, directory);
        Directory.CreateDirectory(Path.Combine(root, ".craft-plugin"));
        File.WriteAllText(Path.Combine(root, ".craft-plugin", "plugin.json"),
            """{"schemaVersion":1,"id":"demo","version":"1.0.0","displayName":"Demo","description":"Test plugin","capabilities":["mcp"]}""");
        File.WriteAllText(Path.Combine(root, ".mcp.json"),
            JsonSerializer.Serialize(new { mcpServers = new { demo = server } }));
        var parsed = PluginManifestParser.Load(root);
        Assert.NotNull(parsed.Manifest);
        return new DiscoveredPlugin(parsed.Manifest, PluginDiscoverySourceKind.Workspace, root, Enabled: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
