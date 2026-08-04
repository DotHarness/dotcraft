using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Context.Compaction;

namespace DotCraft.Configuration;

internal static class ModelCatalog
{
    public const int DefaultContextWindow = 256_000;
    public const string FileName = "models.json";

    private const int MinContextWindow = 1_000;
    private const string EmbeddedResourceName = "DotCraft.Resources.models.json";

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
                ModelProviderResolver.ResolveConfiguredModel(config),
                CatalogPathForConfig(globalConfigPath),
                CatalogPathForConfig(workspaceConfigPath)),
            config.Compaction.MaxContextWindow);
    }

    public static CompactionConfig ResolveCompactionConfig(AppConfig config, string? model)
        => ResolveCompactionConfig(config, model, config.Compaction.ContextWindowMode);

    public static CompactionConfig ResolveCompactionConfig(
        AppConfig config,
        string? model,
        ContextWindowMode contextWindowMode)
    {
        ArgumentNullException.ThrowIfNull(config);

        var compaction = ResolveDefaultCompactionConfig(config, model);
        if (contextWindowMode != ContextWindowMode.Max)
            return compaction;

        var resolution = ResolveDetailed(
            model,
            CatalogPathForConfig(config.GlobalConfigPath),
            CatalogPathForConfig(config.WorkspaceConfigPath));
        if (resolution.HasExplicitMatch && resolution.ContextWindow > compaction.ContextWindow)
            compaction.ContextWindow = resolution.ContextWindow;

        return compaction;
    }

    public static int Resolve(string? model, string? globalCatalogPath = null, string? workspaceCatalogPath = null)
        => ResolveDetailed(model, globalCatalogPath, workspaceCatalogPath).ContextWindow;

    public static ModelContextWindowResolution ResolveDetailed(
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        var match = ResolveModelWindow(model, catalog.Models);
        return match == null
            ? new ModelContextWindowResolution(
                catalog.DefaultContextWindow ?? DefaultContextWindow,
                HasExplicitMatch: false,
                MatchedPattern: null,
                MatchKind: null)
            : new ModelContextWindowResolution(
                match.Value,
                HasExplicitMatch: true,
                match.Pattern,
                match.MatchKind);
    }

    public static ModelContextWindowCapability ResolveContextWindowCapability(AppConfig config, string? model)
    {
        ArgumentNullException.ThrowIfNull(config);

        var defaultCompaction = ResolveDefaultCompactionConfig(config, model);
        var resolution = ResolveDetailed(
            model,
            CatalogPathForConfig(config.GlobalConfigPath),
            CatalogPathForConfig(config.WorkspaceConfigPath));
        var supportsMax = resolution.HasExplicitMatch && resolution.ContextWindow > defaultCompaction.ContextWindow;

        return new ModelContextWindowCapability(
            CatalogWindow: resolution.ContextWindow,
            ConfiguredWindow: defaultCompaction.ContextWindow,
            SupportsMax: supportsMax,
            MaxWindow: supportsMax ? resolution.ContextWindow : defaultCompaction.ContextWindow,
            HasExplicitCatalogMatch: resolution.HasExplicitMatch,
            MatchedPattern: resolution.MatchedPattern,
            MatchKind: resolution.MatchKind);
    }

    public static bool SupportsFast(AppConfig config, string? protocol, string? model)
    {
        ArgumentNullException.ThrowIfNull(config);

        return SupportsFast(
            protocol,
            model,
            CatalogPathForConfig(config.GlobalConfigPath),
            CatalogPathForConfig(config.WorkspaceConfigPath));
    }

    internal static bool SupportsFast(
        string? protocol,
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        var match = ResolveModelCapability(model, catalog.Models, static entry => entry.HasFastDeclaration);
        return match?.Value.Fast?.Matches(protocol) == true;
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

                if (model.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var entry = new ModelData();
                if (TryGetProperty(model.Value, "contextWindow", out var contextWindowElement)
                    && TryReadContextWindow(contextWindowElement, out var contextWindow))
                {
                    entry.ContextWindow = contextWindow;
                }

                if (TryGetProperty(model.Value, "fast", out var fastElement))
                {
                    if (fastElement.ValueKind == JsonValueKind.Null)
                    {
                        entry.HasFastDeclaration = true;
                    }
                    else if (TryReadFastCapability(fastElement, out var fast))
                    {
                        entry.HasFastDeclaration = true;
                        entry.Fast = fast;
                    }
                }

                if (entry.ContextWindow is not null || entry.HasFastDeclaration)
                    catalog.Models[model.Name.Trim()] = entry;
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

    private static CompactionConfig ResolveDefaultCompactionConfig(AppConfig config, string? model)
    {
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

    private static CatalogMatch<int>? ResolveModelWindow(
        string? model,
        IReadOnlyDictionary<string, ModelData> models)
    {
        var match = ResolveModelCapability(model, models, static entry => entry.ContextWindow is not null);
        return match is null
            ? null
            : new CatalogMatch<int>(match.Pattern, match.Value.ContextWindow!.Value, match.MatchKind);
    }

    private static CatalogMatch<ModelData>? ResolveModelCapability(
        string? model,
        IReadOnlyDictionary<string, ModelData> models,
        Func<ModelData, bool> predicate)
    {
        var normalizedModel = model?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
            return null;

        var prefixMatch = ResolveByLongestPrefix(normalizedModel, models, predicate);
        var suffixMatch = ResolveNamespacedSuffixes(normalizedModel, models, predicate);
        if (prefixMatch is null)
            return suffixMatch;
        if (suffixMatch is null || prefixMatch.Pattern.Length >= suffixMatch.Pattern.Length)
            return prefixMatch;
        return suffixMatch;
    }

    private static CatalogMatch<ModelData>? ResolveNamespacedSuffixes(
        string model,
        IReadOnlyDictionary<string, ModelData> models,
        Func<ModelData, bool> predicate)
    {
        var bestLength = -1;
        CatalogMatch<ModelData>? bestMatch = null;
        for (var i = 0; i < model.Length; i++)
        {
            if (model[i] != '/' || i == model.Length - 1)
                continue;

            var suffix = model[(i + 1)..];
            foreach (var (pattern, entry) in models)
            {
                if (!predicate(entry))
                    continue;
                if (!suffix.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (pattern.Length <= bestLength)
                    continue;

                bestLength = pattern.Length;
                bestMatch = new CatalogMatch<ModelData>(pattern, entry, "namespacedSuffix");
            }
        }

        return bestMatch;
    }

    private static CatalogMatch<ModelData>? ResolveByLongestPrefix(
        string model,
        IReadOnlyDictionary<string, ModelData> models,
        Func<ModelData, bool> predicate)
    {
        var bestLength = -1;
        CatalogMatch<ModelData>? bestMatch = null;
        foreach (var (pattern, entry) in models)
        {
            if (!predicate(entry))
                continue;
            if (!model.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pattern.Length <= bestLength)
                continue;

            bestLength = pattern.Length;
            bestMatch = new CatalogMatch<ModelData>(pattern, entry, "prefix");
        }

        return bestMatch;
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

    private static bool TryReadFastCapability(JsonElement element, out FastCapabilityData capability)
    {
        capability = new FastCapabilityData();
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        ReadStringArray(element, "protocols", capability.Protocols);
        return capability.Protocols.Count > 0;
    }

    private static void ReadStringArray(JsonElement root, string propertyName, ISet<string> target)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                target.Add(item.GetString()!.Trim());
        }
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

        public Dictionary<string, ModelData> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public static CatalogData WithDefault() => new() { DefaultContextWindow = ModelCatalog.DefaultContextWindow };

        public void MergeFrom(CatalogData other)
        {
            if (other.DefaultContextWindow is not null)
                DefaultContextWindow = other.DefaultContextWindow;

            foreach (var (model, source) in other.Models)
            {
                if (!Models.TryGetValue(model, out var target))
                {
                    Models[model] = source.Clone();
                    continue;
                }

                if (source.ContextWindow is not null)
                    target.ContextWindow = source.ContextWindow;
                if (source.HasFastDeclaration)
                {
                    target.HasFastDeclaration = true;
                    target.Fast = source.Fast?.Clone();
                }
            }
        }
    }

    internal sealed class ModelData
    {
        public int? ContextWindow { get; set; }

        public bool HasFastDeclaration { get; set; }

        public FastCapabilityData? Fast { get; set; }

        public ModelData Clone() => new()
        {
            ContextWindow = ContextWindow,
            HasFastDeclaration = HasFastDeclaration,
            Fast = Fast?.Clone()
        };
    }

    internal sealed class FastCapabilityData
    {
        public HashSet<string> Protocols { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool Matches(string? protocol)
        {
            var normalizedProtocol = ModelProviderProtocols.Normalize(protocol);
            return Protocols.Contains(normalizedProtocol);
        }

        public FastCapabilityData Clone()
        {
            var clone = new FastCapabilityData();
            foreach (var protocol in Protocols)
                clone.Protocols.Add(protocol);
            return clone;
        }
    }

    private sealed record CatalogMatch<T>(string Pattern, T Value, string MatchKind);
}

internal sealed record ModelContextWindowResolution(
    int ContextWindow,
    bool HasExplicitMatch,
    string? MatchedPattern,
    string? MatchKind);

internal sealed record ModelContextWindowCapability(
    int CatalogWindow,
    int ConfiguredWindow,
    bool SupportsMax,
    int MaxWindow,
    bool HasExplicitCatalogMatch,
    string? MatchedPattern,
    string? MatchKind);
