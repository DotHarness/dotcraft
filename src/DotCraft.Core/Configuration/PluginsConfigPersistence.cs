using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;

namespace DotCraft.Configuration;

public static class PluginsConfigPersistence
{
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

        if (AtomicConfigDocument.Value(root, "Plugins") is not JsonObject pluginsObj)
            return [];
        if (AtomicConfigDocument.Value(pluginsObj, "PluginRegistries") is not JsonArray registries)
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
        var array = new JsonArray();
        foreach (var registry in registries)
        {
            if (string.IsNullOrWhiteSpace(registry.Url))
                continue;
            array.Add(JsonSerializer.SerializeToNode(registry, RegistrySerializerOptions));
        }

        AtomicConfigDocument.Update(configPath, root =>
        {
            var plugins = AtomicConfigDocument.Object(root, "Plugins");
            plugins[AtomicConfigDocument.Key(plugins, "PluginRegistries") ?? "PluginRegistries"] = array;
        });
    }

    private static readonly JsonSerializerOptions RegistrySerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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
}
