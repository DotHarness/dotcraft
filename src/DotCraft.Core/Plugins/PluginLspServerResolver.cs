using DotCraft.Configuration;
using DotCraft.Lsp;
using PluginDiagnostic = DotCraft.Plugins.PluginDiagnostic;

namespace DotCraft.Plugins;

public sealed record PluginLspServerSummary(
    string PluginId,
    string Name,
    string RuntimeName,
    string Transport,
    bool Enabled,
    bool Active,
    IReadOnlyList<string> Extensions,
    string? ShadowedBy);

public static class PluginLspServerResolver
{
    public static IReadOnlyList<LspServerConfig> LoadEffectiveServers(
        AppConfig config,
        string workspacePath,
        string botPath,
        out IReadOnlyList<PluginDiagnostic> diagnostics)
    {
        var allDiagnostics = new List<PluginDiagnostic>();
        var discovery = new PluginDiscoveryService().Discover(config, workspacePath, botPath);
        allDiagnostics.AddRange(discovery.Diagnostics);

        var pluginServers = new List<LspServerConfig>();
        foreach (var plugin in discovery.Plugins)
            pluginServers.AddRange(PluginLspServerLoader.LoadPluginServers(plugin, allDiagnostics));

        diagnostics = allDiagnostics;
        return BuildEffectiveServers(config.LspServers, pluginServers);
    }

    public static IReadOnlyList<LspServerConfig> BuildEffectiveServers(
        IEnumerable<LspServerConfig> workspaceServers,
        IEnumerable<LspServerConfig> pluginServers)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<LspServerConfig>();

        foreach (var server in workspaceServers)
        {
            if (string.IsNullOrWhiteSpace(server.Name) || !names.Add(server.Name))
                continue;

            var clone = server.Clone();
            clone.Origin = LspServerOrigin.Workspace();
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

    public static IReadOnlyDictionary<string, IReadOnlyList<PluginLspServerSummary>> BuildPluginLspServerSummaries(
        IEnumerable<DiscoveredPlugin> plugins,
        IEnumerable<LspServerConfig> workspaceServers,
        List<PluginDiagnostic> diagnostics,
        IReadOnlyDictionary<string, IReadOnlyList<LspServerConfig>>? pluginServersByPluginId = null,
        bool lspToolEnabled = true)
    {
        var workspaceNames = workspaceServers
            .Where(server => !string.IsNullOrWhiteSpace(server.Name))
            .Select(server => server.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var effectiveNames = new HashSet<string>(workspaceNames, StringComparer.OrdinalIgnoreCase);
        var summaries = new Dictionary<string, IReadOnlyList<PluginLspServerSummary>>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in plugins)
        {
            var pluginSummaries = new List<PluginLspServerSummary>();
            var servers = pluginServersByPluginId?.TryGetValue(plugin.Manifest.Id, out var preloaded) == true
                ? preloaded
                : PluginLspServerLoader.LoadPluginServers(plugin, diagnostics);

            foreach (var server in servers)
            {
                var declaredName = server.Origin.DeclaredName ?? server.Name;
                string? shadowedBy = null;
                if (effectiveNames.Contains(server.Name))
                    shadowedBy = workspaceNames.Contains(server.Name) ? "workspace" : "plugin";

                var active = lspToolEnabled
                             && plugin.Installed
                             && plugin.Enabled
                             && server.Enabled
                             && shadowedBy == null;

                if (active)
                    effectiveNames.Add(server.Name);

                pluginSummaries.Add(new PluginLspServerSummary(
                    plugin.Manifest.Id,
                    declaredName,
                    server.Name,
                    server.NormalizedTransport,
                    server.Enabled,
                    active,
                    NormalizeExtensions(server.ExtensionToLanguage.Keys),
                    shadowedBy));
            }

            summaries[plugin.Manifest.Id] = pluginSummaries;
        }

        return summaries;
    }

    private static IReadOnlyList<string> NormalizeExtensions(IEnumerable<string> extensions) =>
        extensions
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
