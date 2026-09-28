using System.Text.Json.Nodes;
using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.AppServer;

internal sealed partial class PluginRequestHandler
{
    private string PluginDataPath(string scope) => scope switch
    {
        "workspace" => workspaceCraftPath ?? throw AppServerErrors.InvalidParams("Workspace configuration is unavailable."),
        "user" => Path.GetDirectoryName(workspaceConfig.RequirePersonalConfigPath("plugin management"))!,
        _ => throw AppServerErrors.InvalidParams("Unknown plugin scope.")
    };

    private string PluginScope(DiscoveredPlugin plugin) => plugin.SourceKind == PluginDiscoverySourceKind.UserGlobal ? "user" : "workspace";

    private void SetScopedPluginEnabled(DiscoveredPlugin plugin, bool enabled)
    {
        var data = PluginDataPath(PluginScope(plugin));
        AtomicConfigDocument.Update(Path.Combine(data, "config.json"), root =>
        {
            var plugins = AtomicConfigDocument.Object(root, "Plugins");
            var key = AtomicConfigDocument.Key(plugins, "DisabledPlugins") ?? "DisabledPlugins";
            var values = plugins[key] as JsonArray ?? new JsonArray();
            var disabled = values.Select(v => v!.GetValue<string>()).Where(id => !PluginIds.EqualsCanonical(id, plugin.Manifest.Id)).ToList();
            if (!enabled) disabled.Add(plugin.Manifest.Id);
            plugins[key] = new JsonArray(disabled.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        });
        if (appConfigMonitor != null) appConfigMonitor.Current.Plugins = workspaceConfig.LoadCurrentMergedConfig().Plugins;
        AtomicConfigDocument.Update(Path.Combine(data, "imports", "revision.json"), root => root["revision"] = Guid.NewGuid().ToString("N"));
    }
}
