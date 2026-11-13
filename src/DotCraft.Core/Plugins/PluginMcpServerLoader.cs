using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Mcp;
using DotCraft.Workspaces;
using McpServerConfig = DotCraft.Mcp.McpServerConfig;
using McpServerOrigin = DotCraft.Mcp.McpServerOrigin;

namespace DotCraft.Plugins;

/// <summary>
/// Loads MCP server declarations contributed by enabled plugins.
/// </summary>
public static class PluginMcpServerLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new McpServerConfigListConverter() }
    };

    public static IReadOnlyList<McpServerConfig> LoadEnabledPluginServers(
        AppConfig config,
        string workspacePath,
        string botPath,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService().Discover(config, workspacePath, botPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var userDataPath = string.IsNullOrWhiteSpace(config.GlobalConfigPath)
            ? null : Path.GetDirectoryName(config.GlobalConfigPath);
        var paths = DotCraftPaths.Create(workspacePath, botPath, userDataPath);
        var servers = new List<McpServerConfig>();
        foreach (var plugin in discovery.Plugins)
            servers.AddRange(LoadPluginServers(plugin, allDiagnostics, PluginDataPaths.Resolve(paths, plugin.Manifest.Id)));

        diagnostics = allDiagnostics;
        return servers;
    }

    public static IReadOnlyList<McpServerConfig> LoadPluginServers(
        DiscoveredPlugin plugin,
        List<PluginDiagnostic> diagnostics,
        string? pluginDataPath = null)
    {
        var manifest = plugin.Manifest;
        var path = manifest.McpServersPath;
        if (string.IsNullOrWhiteSpace(path))
            return [];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            root = doc.RootElement.Clone();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginMcpConfig",
                $"Failed to read plugin MCP config: {ex.Message}",
                manifest.Id,
                path: path));
            return [];
        }

        var serverRoot = TryGetObjectProperty(root, "mcpServers", out var nested)
            ? nested
            : root;

        List<McpServerConfig>? parsed;
        try
        {
            parsed = serverRoot.Deserialize<List<McpServerConfig>>(JsonOptions);
        }
        catch (JsonException ex)
        {
            diagnostics.Add(PluginDiagnostic.Error(
                "InvalidPluginMcpConfig",
                $"Failed to parse plugin MCP config: {ex.Message}",
                manifest.Id,
                path: path));
            return [];
        }

        if (parsed is not { Count: > 0 })
            return [];

        var result = new List<McpServerConfig>();
        foreach (var server in parsed)
        {
            var declaredName = server.Name.Trim();
            if (string.IsNullOrWhiteSpace(declaredName))
            {
                diagnostics.Add(PluginDiagnostic.Warning(
                    "InvalidPluginMcpServer",
                    "Plugin MCP server declaration is missing a name.",
                    manifest.Id,
                    path: path));
                continue;
            }

            var clone = server.Clone();
            clone.Name = $"{manifest.Id}:{declaredName}";
            clone.Origin = McpServerOrigin.Plugin(
                manifest.Id,
                manifest.Interface?.DisplayName ?? manifest.DisplayName,
                declaredName);
            if (clone.NormalizedTransport == "stdio")
                ExpandLaunchConfiguration(clone, manifest.RootPath, pluginDataPath);
            if (!string.IsNullOrWhiteSpace(clone.Cwd))
                clone.Cwd = Path.GetFullPath(Path.Combine(manifest.RootPath, clone.Cwd));
            result.Add(clone);
        }

        return result;
    }

    private static void ExpandLaunchConfiguration(McpServerConfig server, string pluginRoot, string? pluginDataPath)
    {
        server.Command = PluginVariables.Expand(server.Command, pluginRoot, pluginDataPath);
        if (Path.IsPathRooted(server.Command))
            server.Command = Path.GetFullPath(server.Command);
        server.Arguments = server.Arguments.Select(value => PluginVariables.Expand(value, pluginRoot, pluginDataPath)).ToList();
        if (server.Cwd is not null)
            server.Cwd = PluginVariables.Expand(server.Cwd, pluginRoot, pluginDataPath);
        foreach (var key in server.EnvironmentVariables.Keys.ToArray())
            server.EnvironmentVariables[key] = PluginVariables.Expand(server.EnvironmentVariables[key], pluginRoot, pluginDataPath);

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var (name, value) in new[] { ("PLUGIN_ROOT", pluginRoot), ("PLUGIN_DATA", pluginDataPath) })
        {
            if (value is null)
                continue;
            foreach (var key in server.EnvironmentVariables.Keys.Where(key => key.Equals(name, comparison)).ToArray())
                server.EnvironmentVariables.Remove(key);
            server.EnvVars.RemoveAll(key => key.Equals(name, comparison));
            server.EnvironmentVariables[name] = value;
        }
    }

    private static bool TryGetObjectProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
