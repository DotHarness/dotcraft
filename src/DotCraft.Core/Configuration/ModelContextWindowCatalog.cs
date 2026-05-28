using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;

namespace DotCraft.Configuration;

internal static class ModelContextWindowCatalog
{
    public const int DefaultContextWindow = 256_000;
    public const string FileName = "model-context-windows.json";

    private const int MinContextWindow = 1_000;
    private const string EmbeddedResourceName = "DotCraft.Resources.model-context-windows.json";

    public static void ApplyToConfig(
        AppConfig config,
        JsonNode mergedConfig,
        string? globalConfigPath,
        string? workspaceConfigPath)
    {
        var hasExplicitContextWindow = HasExplicitCompactionContextWindow(mergedConfig);
        config.CompactionContextWindowExplicit = hasExplicitContextWindow;
        config.GlobalConfigPath = globalConfigPath;
        config.WorkspaceConfigPath = workspaceConfigPath;

        if (hasExplicitContextWindow)
            return;

        config.Compaction.ContextWindow = ApplyMaxContextWindow(
            Resolve(
                config.Model,
                CatalogPathForConfig(globalConfigPath),
                CatalogPathForConfig(workspaceConfigPath)),
            config.Compaction.MaxContextWindow);
    }

    public static CompactionConfig ResolveCompactionConfig(AppConfig config, string? model)
    {
        ArgumentNullException.ThrowIfNull(config);

        var compaction = config.Compaction.Clone();
        if (config.CompactionContextWindowExplicit)
            return compaction;

        compaction.ContextWindow = ApplyMaxContextWindow(
            Resolve(
                model,
                CatalogPathForConfig(config.GlobalConfigPath),
                CatalogPathForConfig(config.WorkspaceConfigPath)),
            compaction.MaxContextWindow);
        return compaction;
    }

    public static int Resolve(string? model, string? globalCatalogPath = null, string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        return ResolveModelWindow(model, catalog.Models)
            ?? catalog.DefaultContextWindow
            ?? DefaultContextWindow;
    }

    internal static CatalogData LoadJson(string json)
    {
        var catalog = new CatalogData();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return catalog;

        if (TryGetProperty(root, "defaultContextWindow", out var defaultElement)
            && TryReadContextWindow(defaultElement, out var defaultContextWindow))
        {
            catalog.DefaultContextWindow = defaultContextWindow;
        }

        if (TryGetProperty(root, "models", out var modelsElement)
            && modelsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var model in modelsElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(model.Name))
                    continue;

                if (TryReadContextWindow(model.Value, out var contextWindow))
                    catalog.Models[model.Name.Trim()] = contextWindow;
            }
        }

        return catalog;
    }

    internal static bool HasExplicitCompactionContextWindow(JsonNode node)
    {
        if (node is not JsonObject root)
            return false;

        var compaction = TryGetObject(root, "Compaction");
        if (compaction is null)
            return false;

        return compaction.Any(property => IsContextWindowProperty(property.Key));
    }

    private static CatalogData LoadBuiltInCatalog()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName);
        if (stream is null)
            return CatalogData.WithDefault();

        using var reader = new StreamReader(stream);
        try
        {
            var catalog = LoadJson(reader.ReadToEnd());
            if (catalog.DefaultContextWindow is null)
                catalog.DefaultContextWindow = DefaultContextWindow;
            return catalog;
        }
        catch (JsonException)
        {
            return CatalogData.WithDefault();
        }
    }

    private static void MergeFile(CatalogData target, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var overrideCatalog = LoadJson(File.ReadAllText(path));
            target.MergeFrom(overrideCatalog);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private static int? ResolveModelWindow(string? model, IReadOnlyDictionary<string, int> models)
    {
        var normalizedModel = model?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
            return null;

        return ResolveByLongestPrefix(normalizedModel, models)
            ?? ResolveNamespacedSuffixes(normalizedModel, models);
    }

    private static int? ResolveNamespacedSuffixes(string model, IReadOnlyDictionary<string, int> models)
    {
        var bestLength = -1;
        int? bestValue = null;
        for (var i = 0; i < model.Length; i++)
        {
            if (model[i] != '/' || i == model.Length - 1)
                continue;

            var suffix = model[(i + 1)..];
            foreach (var (pattern, contextWindow) in models)
            {
                if (!suffix.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (pattern.Length <= bestLength)
                    continue;

                bestLength = pattern.Length;
                bestValue = contextWindow;
            }
        }

        return bestValue;
    }

    private static int? ResolveByLongestPrefix(string model, IReadOnlyDictionary<string, int> models)
    {
        var bestLength = -1;
        int? bestValue = null;
        foreach (var (pattern, contextWindow) in models)
        {
            if (!model.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pattern.Length <= bestLength)
                continue;

            bestLength = pattern.Length;
            bestValue = contextWindow;
        }

        return bestValue;
    }

    private static string? CatalogPathForConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return null;

        var directory = Path.GetDirectoryName(configPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, FileName);
    }

    private static int ApplyMaxContextWindow(int contextWindow, int maxContextWindow)
    {
        if (maxContextWindow < MinContextWindow)
            return contextWindow;

        return Math.Min(contextWindow, maxContextWindow);
    }

    private static bool TryReadContextWindow(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var parsed))
            return false;

        if (parsed < MinContextWindow)
            return false;

        value = parsed;
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static JsonObject? TryGetObject(JsonObject root, string key)
    {
        foreach (var property in root)
        {
            if (string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase)
                && property.Value is JsonObject obj)
            {
                return obj;
            }
        }

        return null;
    }

    private static bool IsContextWindowProperty(string propertyName)
    {
        var normalized = propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return string.Equals(normalized, "ContextWindow", StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class CatalogData
    {
        public int? DefaultContextWindow { get; set; }

        public Dictionary<string, int> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static CatalogData WithDefault() => new() { DefaultContextWindow = ModelContextWindowCatalog.DefaultContextWindow };

        public void MergeFrom(CatalogData other)
        {
            if (other.DefaultContextWindow is not null)
                DefaultContextWindow = other.DefaultContextWindow;

            foreach (var (model, contextWindow) in other.Models)
                Models[model] = contextWindow;
        }
    }
}
