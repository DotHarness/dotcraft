using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;

namespace DotCraft.Configuration;

public static class PluginsConfigPersistence
{
    public static void WriteWorkspaceDisabledPlugins(string craftPath, IReadOnlyList<string> disabledPlugins)
    {
        var (path, root) = LoadWorkspaceConfig(craftPath);
        var pluginsObj = root["Plugins"] as JsonObject ?? new JsonObject();
        var arr = new JsonArray();
        foreach (var pluginId in NormalizeDisabledPluginIds(disabledPlugins))
            arr.Add(pluginId);
        pluginsObj["DisabledPlugins"] = arr;
        root["Plugins"] = pluginsObj;
        SaveWorkspaceConfig(path, root);
    }

    public static void WriteWorkspacePluginRoots(string craftPath, IReadOnlyList<string> pluginRoots)
    {
        var (path, root) = LoadWorkspaceConfig(craftPath);
        var pluginsObj = root["Plugins"] as JsonObject ?? new JsonObject();
        var arr = new JsonArray();
        foreach (var pluginRoot in NormalizePluginRoots(pluginRoots))
            arr.Add(pluginRoot);
        pluginsObj["PluginRoots"] = arr;
        root["Plugins"] = pluginsObj;
        SaveWorkspaceConfig(path, root);
    }

    private static (string Path, JsonObject Root) LoadWorkspaceConfig(string craftPath)
    {
        var path = Path.Combine(craftPath, "config.json");
        if (!File.Exists(path))
            return (path, new JsonObject());

        var text = File.ReadAllText(path);
        return (path, (JsonObject)(JsonNode.Parse(text) ?? new JsonObject()));
    }

    private static void SaveWorkspaceConfig(string path, JsonObject root)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static IReadOnlyList<string> NormalizeDisabledPluginIds(IEnumerable<string> disabledPlugins)
    {
        var result = new List<string>();
        foreach (var pluginId in disabledPlugins)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                continue;

            var canonical = PluginIds.Canonicalize(pluginId.Trim());
            if (!result.Contains(canonical, StringComparer.OrdinalIgnoreCase))
                result.Add(canonical);
        }

        return result;
    }

    public static IReadOnlyList<string> NormalizePluginRoots(IEnumerable<string> pluginRoots)
    {
        var result = new List<string>();
        foreach (var pluginRoot in pluginRoots)
        {
            if (string.IsNullOrWhiteSpace(pluginRoot))
                continue;

            var fullPath = Path.GetFullPath(pluginRoot.Trim());
            if (!result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                result.Add(fullPath);
        }

        return result;
    }
}
