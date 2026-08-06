using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

internal static class ModelThinkingAdapterCatalog
{
    public const string FileName = "model-thinking-adapters.json";

    private const string EmbeddedResourceName = "DotCraft.Resources.model-thinking-adapters.json";

    public static bool ShouldApplyDeepThinking(
        string? endpoint,
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        return MatchesModel(model, catalog.DeepThinking.Models)
            || MatchesEndpoint(endpoint, catalog.DeepThinking.Endpoints);
    }

    public static AnthropicThinkingAdapterData? ResolveAnthropicThinkingAdapter(
        string? endpoint,
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        return catalog.AnthropicThinking.Resolve(endpoint, model);
    }

    public static AnthropicMessageContentAdapterData? ResolveAnthropicMessageContentAdapter(
        string? endpoint,
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        return catalog.AnthropicMessageContent.Resolve(endpoint, model);
    }

    public static ReasoningCapabilityData? ResolveReasoningCapability(
        string? protocol,
        string? endpoint,
        string? model,
        string? globalCatalogPath = null,
        string? workspaceCatalogPath = null)
    {
        var catalog = LoadBuiltInCatalog();
        MergeFile(catalog, globalCatalogPath);
        MergeFile(catalog, workspaceCatalogPath);

        return catalog.ReasoningCapabilities.Resolve(protocol, endpoint, model);
    }

    internal static CatalogData LoadJson(string json)
    {
        var catalog = new CatalogData();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return catalog;

        if (TryGetProperty(root, "deepThinking", out var deepThinkingElement)
            && deepThinkingElement.ValueKind == JsonValueKind.Object)
        {
            ReadStringArray(deepThinkingElement, "models", catalog.DeepThinking.Models);
            ReadStringArray(deepThinkingElement, "endpoints", catalog.DeepThinking.Endpoints);
        }

        if (TryGetProperty(root, "anthropicThinking", out var anthropicThinkingElement)
            && anthropicThinkingElement.ValueKind == JsonValueKind.Object
            && TryGetProperty(anthropicThinkingElement, "adapters", out var adaptersElement)
            && adaptersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var adapterElement in adaptersElement.EnumerateArray())
            {
                if (adapterElement.ValueKind != JsonValueKind.Object)
                    continue;

                var adapter = new AnthropicThinkingAdapterData();
                ReadStringArray(adapterElement, "models", adapter.Models);
                ReadStringArray(adapterElement, "endpoints", adapter.Endpoints);

                if (TryGetProperty(adapterElement, "thinking", out var thinkingElement)
                    && thinkingElement.ValueKind == JsonValueKind.Object)
                {
                    adapter.ThinkingType = ReadString(thinkingElement, "type");
                    adapter.ThinkingDisplay = ReadString(thinkingElement, "display");
                }

                if (TryGetProperty(adapterElement, "outputConfig", out var outputConfigElement)
                    && outputConfigElement.ValueKind == JsonValueKind.Object)
                {
                    adapter.OutputConfigEffort = ReadString(outputConfigElement, "effort");
                    ReadStringMap(outputConfigElement, "effortMap", adapter.OutputConfigEffortMap);
                }

                if (adapter.HasMatch && adapter.HasRequestShape)
                    catalog.AnthropicThinking.Adapters.Add(adapter);
            }
        }

        if (TryGetProperty(root, "anthropicMessageContent", out var anthropicMessageContentElement)
            && anthropicMessageContentElement.ValueKind == JsonValueKind.Object
            && TryGetProperty(anthropicMessageContentElement, "adapters", out var messageAdaptersElement)
            && messageAdaptersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var adapterElement in messageAdaptersElement.EnumerateArray())
            {
                if (adapterElement.ValueKind != JsonValueKind.Object)
                    continue;

                var adapter = new AnthropicMessageContentAdapterData();
                ReadStringArray(adapterElement, "models", adapter.Models);
                ReadStringArray(adapterElement, "endpoints", adapter.Endpoints);

                if (TryGetProperty(adapterElement, "reasoningHistory", out var reasoningHistoryElement)
                    && reasoningHistoryElement.ValueKind == JsonValueKind.Object)
                {
                    adapter.ReasoningHistoryBlockType = ReadString(reasoningHistoryElement, "blockType");
                }

                if (adapter.HasMatch && adapter.HasReasoningHistory)
                    catalog.AnthropicMessageContent.Adapters.Add(adapter);
            }
        }

        if (TryGetProperty(root, "reasoningCapabilities", out var reasoningCapabilitiesElement)
            && reasoningCapabilitiesElement.ValueKind == JsonValueKind.Object
            && TryGetProperty(reasoningCapabilitiesElement, "adapters", out var capabilityAdaptersElement)
            && capabilityAdaptersElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var adapterElement in capabilityAdaptersElement.EnumerateArray())
            {
                if (adapterElement.ValueKind != JsonValueKind.Object)
                    continue;

                var adapter = new ReasoningCapabilityData();
                ReadStringArray(adapterElement, "protocols", adapter.Protocols);
                ReadStringArray(adapterElement, "models", adapter.Models);
                ReadStringArray(adapterElement, "endpoints", adapter.Endpoints);
                if (TryGetProperty(adapterElement, "supportsDisable", out var supportsDisableElement)
                    && supportsDisableElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    adapter.SupportsDisable = supportsDisableElement.GetBoolean();
                }

                ReadReasoningEffortArray(adapterElement, "supportedEfforts", adapter.SupportedEfforts);
                if (TryReadReasoningEffort(adapterElement, "defaultEffort", out var defaultEffort))
                    adapter.DefaultEffort = defaultEffort;

                ReadReasoningOutputArray(adapterElement, "supportedOutputs", adapter.SupportedOutputs);
                if (TryReadReasoningOutput(adapterElement, "defaultOutput", out var defaultOutput))
                    adapter.DefaultOutput = defaultOutput;

                adapter.NormalizeDefaults();
                if (adapter.HasMatch && adapter.SupportedEfforts.Count > 0)
                    catalog.ReasoningCapabilities.Adapters.Add(adapter);
            }
        }

        return catalog;
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
            catalog.MergeDefaults();
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
            target.MergeFrom(LoadJson(File.ReadAllText(path)));
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

    private static bool MatchesModel(string? model, IReadOnlyCollection<string> patterns)
    {
        var normalizedModel = model?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedModel))
            return false;

        return MatchesLongestPrefix(normalizedModel, patterns)
            || MatchesNamespacedSuffixes(normalizedModel, patterns);
    }

    private static bool MatchesEndpoint(string? endpoint, IReadOnlyCollection<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return false;

        var endpointName = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Host
            : endpoint;

        return patterns.Any(pattern =>
            !string.IsNullOrWhiteSpace(pattern) &&
            endpointName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesNamespacedSuffixes(string model, IReadOnlyCollection<string> patterns)
    {
        for (var i = 0; i < model.Length; i++)
        {
            if (model[i] != '/' || i == model.Length - 1)
                continue;

            if (MatchesLongestPrefix(model[(i + 1)..], patterns))
                return true;
        }

        return false;
    }

    private static bool MatchesLongestPrefix(string value, IReadOnlyCollection<string> patterns)
    {
        var bestLength = -1;
        foreach (var pattern in patterns)
        {
            if (!value.StartsWith(pattern, StringComparison.OrdinalIgnoreCase))
                continue;

            if (pattern.Length > bestLength)
                bestLength = pattern.Length;
        }

        return bestLength >= 0;
    }

    private static void ReadStringArray(JsonElement root, string propertyName, ISet<string> target)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var value = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                target.Add(value);
        }
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            return null;

        var value = element.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void ReadStringMap(JsonElement root, string propertyName, IDictionary<string, string> target)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            var key = property.Name.Trim();
            var value = property.Value.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
                target[key] = value;
        }
    }

    private static void ReadReasoningEffortArray(
        JsonElement root,
        string propertyName,
        ICollection<ReasoningEffortOptionData> target)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                if (TryParseReasoningEffort(item.GetString(), out var effort))
                    target.Add(new ReasoningEffortOptionData(effort));
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object
                || !TryReadReasoningEffort(item, "effort", out var objectEffort))
            {
                continue;
            }

            target.Add(new ReasoningEffortOptionData(
                objectEffort,
                ReadString(item, "label"),
                ReadString(item, "description")));
        }
    }

    private static void ReadReasoningOutputArray(
        JsonElement root,
        string propertyName,
        ICollection<ReasoningOutput> target)
    {
        if (!TryGetProperty(root, propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return;

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            if (TryParseReasoningOutput(item.GetString(), out var output))
                target.Add(output);
        }
    }

    private static bool TryReadReasoningEffort(JsonElement root, string propertyName, out ReasoningEffort effort)
    {
        effort = default;
        return TryGetProperty(root, propertyName, out var element)
               && element.ValueKind == JsonValueKind.String
               && TryParseReasoningEffort(element.GetString(), out effort);
    }

    private static bool TryReadReasoningOutput(JsonElement root, string propertyName, out ReasoningOutput output)
    {
        output = default;
        return TryGetProperty(root, propertyName, out var element)
               && element.ValueKind == JsonValueKind.String
               && TryParseReasoningOutput(element.GetString(), out output);
    }

    internal static bool TryParseReasoningEffort(string? value, out ReasoningEffort effort)
    {
        var normalized = NormalizeEnumToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            effort = default;
            return false;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out effort);
    }

    internal static bool TryParseReasoningOutput(string? value, out ReasoningOutput output)
    {
        var normalized = NormalizeEnumToken(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            output = default;
            return false;
        }

        return Enum.TryParse(normalized, ignoreCase: true, out output);
    }

    private static string NormalizeEnumToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Concat(value.Trim().Where(ch => ch is not '-' and not '_' and not ' '));
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

    internal static string? CatalogPathForConfig(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return null;

        var directory = Path.GetDirectoryName(configPath);
        return string.IsNullOrWhiteSpace(directory)
            ? null
            : Path.Combine(directory, FileName);
    }

    internal sealed class CatalogData
    {
        public AdapterData DeepThinking { get; } = new();

        public AnthropicThinkingData AnthropicThinking { get; } = new();

        public AnthropicMessageContentData AnthropicMessageContent { get; } = new();

        public ReasoningCapabilitiesData ReasoningCapabilities { get; } = new();

        public static CatalogData WithDefault()
        {
            var catalog = new CatalogData();
            catalog.MergeDefaults();
            return catalog;
        }

        public void MergeDefaults()
        {
            DeepThinking.Models.Add("deepseek");
            DeepThinking.Models.Add("mimo");
            DeepThinking.Endpoints.Add("deepseek");
        }

        public void MergeFrom(CatalogData other)
        {
            foreach (var model in other.DeepThinking.Models)
                DeepThinking.Models.Add(model);

            foreach (var endpoint in other.DeepThinking.Endpoints)
                DeepThinking.Endpoints.Add(endpoint);

            AnthropicThinking.MergeFrom(other.AnthropicThinking);
            AnthropicMessageContent.MergeFrom(other.AnthropicMessageContent);
            ReasoningCapabilities.MergeFrom(other.ReasoningCapabilities);
        }
    }

    internal sealed class AdapterData
    {
        public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class AnthropicThinkingData
    {
        public List<AnthropicThinkingAdapterData> Adapters { get; } = [];

        public AnthropicThinkingAdapterData? Resolve(string? endpoint, string? model)
        {
            for (var i = Adapters.Count - 1; i >= 0; i--)
            {
                var adapter = Adapters[i];
                if (MatchesModel(model, adapter.Models) || MatchesEndpoint(endpoint, adapter.Endpoints))
                    return adapter;
            }

            return null;
        }

        public void MergeFrom(AnthropicThinkingData other)
        {
            Adapters.AddRange(other.Adapters);
        }
    }

    internal sealed class AnthropicThinkingAdapterData
    {
        public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ThinkingType { get; set; }

        public string? ThinkingDisplay { get; set; }

        public string? OutputConfigEffort { get; set; }

        public Dictionary<string, string> OutputConfigEffortMap { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasMatch => Models.Count > 0 || Endpoints.Count > 0;

        public bool HasRequestShape =>
            !string.IsNullOrWhiteSpace(ThinkingType) ||
            !string.IsNullOrWhiteSpace(ThinkingDisplay) ||
            !string.IsNullOrWhiteSpace(OutputConfigEffort);
    }

    internal sealed class AnthropicMessageContentData
    {
        public List<AnthropicMessageContentAdapterData> Adapters { get; } = [];

        public AnthropicMessageContentAdapterData? Resolve(string? endpoint, string? model)
        {
            for (var i = Adapters.Count - 1; i >= 0; i--)
            {
                var adapter = Adapters[i];
                if (MatchesModel(model, adapter.Models) || MatchesEndpoint(endpoint, adapter.Endpoints))
                    return adapter.Clone();
            }

            return null;
        }

        public void MergeFrom(AnthropicMessageContentData other)
        {
            Adapters.AddRange(other.Adapters.Select(static adapter => adapter.Clone()));
        }
    }

    internal sealed class AnthropicMessageContentAdapterData
    {
        public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? ReasoningHistoryBlockType { get; set; }

        public bool HasMatch => Models.Count > 0 || Endpoints.Count > 0;

        public bool HasReasoningHistory => !string.IsNullOrWhiteSpace(ReasoningHistoryBlockType);

        public AnthropicMessageContentAdapterData Clone()
        {
            var clone = new AnthropicMessageContentAdapterData();
            foreach (var model in Models)
                clone.Models.Add(model);
            foreach (var endpoint in Endpoints)
                clone.Endpoints.Add(endpoint);
            clone.ReasoningHistoryBlockType = ReasoningHistoryBlockType;
            return clone;
        }
    }

    internal sealed class ReasoningCapabilitiesData
    {
        public List<ReasoningCapabilityData> Adapters { get; } = [];

        public ReasoningCapabilityData? Resolve(string? protocol, string? endpoint, string? model)
        {
            for (var i = Adapters.Count - 1; i >= 0; i--)
            {
                var adapter = Adapters[i];
                if (adapter.Matches(protocol, endpoint, model))
                    return adapter.Clone();
            }

            return null;
        }

        public void MergeFrom(ReasoningCapabilitiesData other)
        {
            Adapters.AddRange(other.Adapters.Select(adapter => adapter.Clone()));
        }
    }

    internal sealed class ReasoningCapabilityData
    {
        public HashSet<string> Protocols { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Models { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool SupportsDisable { get; set; } = true;

        public List<ReasoningEffortOptionData> SupportedEfforts { get; } = [];

        public ReasoningEffort DefaultEffort { get; set; } = ReasoningEffort.Medium;

        public List<ReasoningOutput> SupportedOutputs { get; } = [];

        public ReasoningOutput DefaultOutput { get; set; } = ReasoningOutput.Full;

        public bool HasMatch => Protocols.Count > 0 || Models.Count > 0 || Endpoints.Count > 0;

        public bool Matches(string? protocol, string? endpoint, string? model)
        {
            var normalizedProtocol = string.IsNullOrWhiteSpace(protocol)
                ? null
                : ModelProviderProtocols.Normalize(protocol);
            if (Protocols.Count > 0
                && (string.IsNullOrWhiteSpace(normalizedProtocol)
                    || (!Protocols.Contains(normalizedProtocol)
                        && !Protocols.Contains(protocol!.Trim()))))
            {
                return false;
            }

            if (Models.Count == 0 && Endpoints.Count == 0)
                return Protocols.Count > 0;

            return MatchesModel(model, Models) || MatchesEndpoint(endpoint, Endpoints);
        }

        public void NormalizeDefaults()
        {
            if (SupportedOutputs.Count == 0)
                SupportedOutputs.Add(ReasoningOutput.Full);

            if (SupportedEfforts.Count == 0)
                return;

            if (SupportedEfforts.All(option => option.Effort != DefaultEffort))
                DefaultEffort = SupportedEfforts[0].Effort;

            if (!SupportedOutputs.Contains(DefaultOutput))
                DefaultOutput = SupportedOutputs[0];
        }

        public ReasoningCapabilityData Clone()
        {
            var clone = new ReasoningCapabilityData
            {
                SupportsDisable = SupportsDisable,
                DefaultEffort = DefaultEffort,
                DefaultOutput = DefaultOutput
            };
            foreach (var protocol in Protocols)
                clone.Protocols.Add(protocol);
            foreach (var model in Models)
                clone.Models.Add(model);
            foreach (var endpoint in Endpoints)
                clone.Endpoints.Add(endpoint);
            clone.SupportedEfforts.AddRange(SupportedEfforts.Select(option => option.Clone()));
            clone.SupportedOutputs.AddRange(SupportedOutputs);
            return clone;
        }
    }

    internal sealed class ReasoningEffortOptionData(
        ReasoningEffort effort,
        string? label = null,
        string? description = null)
    {
        public ReasoningEffort Effort { get; } = effort;

        public string? Label { get; } = label;

        public string? Description { get; } = description;

        public ReasoningEffortOptionData Clone() => new(Effort, Label, Description);
    }
}
