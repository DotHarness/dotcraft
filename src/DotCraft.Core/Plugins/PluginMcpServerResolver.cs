using DotCraft.Configuration;
using DotCraft.Mcp;

namespace DotCraft.Plugins;

public sealed record PluginMcpServerSummary(
    string PluginId,
    string Name,
    string RuntimeName,
    string Transport,
    bool Enabled,
    bool Active,
    string? ShadowedBy);

public static class PluginMcpServerResolver
{
    public static IReadOnlyList<McpServerConfig> LoadEffectiveServers(
        AppConfig config,
        string workspacePath,
        string botPath,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService().Discover(config, workspacePath, botPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var pluginServers = new List<McpServerConfig>();
        foreach (var plugin in discovery.Plugins)
            pluginServers.AddRange(PluginMcpServerLoader.LoadPluginServers(plugin, allDiagnostics));

        diagnostics = allDiagnostics;
        return BuildEffectiveServers(config.McpServers, pluginServers);
    }

    public static IReadOnlyList<McpServerConfig> BuildEffectiveServers(
        IEnumerable<McpServerConfig> workspaceServers,
        IEnumerable<McpServerConfig> pluginServers)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<McpServerConfig>();

        foreach (var server in workspaceServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) || !names.Add(server.Name))
                continue;

            var clone = server.Clone();
            clone.Origin = McpServerOrigin.Workspace();
            result.Add(clone);
        }

        foreach (var server in pluginServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) || !names.Add(server.Name))
                continue;

            result.Add(server.Clone());
        }

        return result;
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<PluginMcpServerSummary>> BuildPluginMcpServerSummaries(
        IEnumerable<DiscoveredPlugin> plugins,
        IEnumerable<McpServerConfig> workspaceServers,
        List<PluginDiagnostic> diagnostics)
    {
        var workspaceNames = workspaceServers
            .Where(server => !string.IsNullOrWhiteSpace(server.Name))
            .Select(server => server.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveNames = new HashSet<string>(workspaceNames, StringComparer.OrdinalIgnoreCase);
        var summaries = new Dictionary<string, IReadOnlyList<PluginMcpServerSummary>>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in plugins)
        {
            var pluginSummaries = new List<PluginMcpServerSummary>();
            var servers = PluginMcpServerLoader.LoadPluginServers(plugin, diagnostics);
            foreach (var server in servers)
            {
                var declaredName = server.Origin.DeclaredName ?? server.Name;
                string? shadowedBy = null;
                if (effectiveNames.Contains(server.Name))
                    shadowedBy = workspaceNames.Contains(server.Name) ? "workspace" : "plugin";

                var active = plugin.Installed
                             && plugin.Enabled
                             && server.Enabled
                             && shadowedBy == null;

                if (active)
                    effectiveNames.Add(server.Name);

                pluginSummaries.Add(new PluginMcpServerSummary(
                    plugin.Manifest.Id,
                    declaredName,
                    server.Name,
                    server.NormalizedTransport,
                    server.Enabled,
                    active,
                    shadowedBy));
            }

            summaries[plugin.Manifest.Id] = pluginSummaries;
        }

        return summaries;
    }
}
