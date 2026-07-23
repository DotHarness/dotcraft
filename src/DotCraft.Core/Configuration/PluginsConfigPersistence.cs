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

    /// <summary>
    /// Reads the marketplace sources recorded in the config file at <paramref name="configPath"/>.
    /// Missing or malformed files read as an empty list rather than failing the caller.
    /// </summary>
    public static IReadOnlyList<AppConfig.PluginRegistryConfig> ReadPluginRegistries(string configPath)
    {
        if (!File.Exists(configPath))
            return [];

        JsonObject root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (FindSection(root, "Plugins") is not JsonObject pluginsObj)
            return [];
        if (FindSection(pluginsObj, "PluginRegistries") is not JsonArray registries)
            return [];

        var result = new List<AppConfig.PluginRegistryConfig>();
        foreach (var entry in registries)
        {
            if (entry is not JsonObject)
                continue;

            try
            {
                var parsed = entry.Deserialize<AppConfig.PluginRegistryConfig>(RegistrySerializerOptions);
                if (parsed != null && !string.IsNullOrWhiteSpace(parsed.Url))
                    result.Add(parsed);
            }
            catch (JsonException)
            {
                // A single malformed entry is skipped so the rest of the sources stay usable.
            }
        }

        return result;
    }

    /// <summary>
    /// Replaces the marketplace sources in the config file at <paramref name="configPath"/>,
    /// preserving every other setting in that file.
    /// </summary>
    public static void WritePluginRegistries(
        string configPath,
        IReadOnlyList<AppConfig.PluginRegistryConfig> registries)
    {
        JsonObject root;
        try
        {
            root = File.Exists(configPath)
                ? JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            root = [];
        }

        var pluginsKey = FindKey(root, "Plugins") ?? "Plugins";
        var pluginsObj = root[pluginsKey] as JsonObject ?? [];
        var registriesKey = FindKey(pluginsObj, "PluginRegistries") ?? "PluginRegistries";

        var array = new JsonArray();
        foreach (var registry in registries)
        {
            if (string.IsNullOrWhiteSpace(registry.Url))
                continue;
            array.Add(JsonSerializer.SerializeToNode(registry, RegistrySerializerOptions));
        }

        pluginsObj[registriesKey] = array;
        root[pluginsKey] = pluginsObj;

        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static readonly JsonSerializerOptions RegistrySerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static JsonNode? FindSection(JsonObject parent, string key)
    {
        var actualKey = FindKey(parent, key);
        return actualKey == null ? null : parent[actualKey];
    }

    private static string? FindKey(JsonObject parent, string key)
    {
        foreach (var pair in parent)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }

        return null;
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
