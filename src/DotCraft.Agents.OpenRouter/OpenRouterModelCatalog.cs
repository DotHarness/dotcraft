using DotCraft.Configuration;

namespace DotCraft.Agents;

public static class OpenRouterModelCatalog
{
    public const string DefaultEndpoint = "https://openrouter.ai/api/v1";

    public static string ApiRoot(string? endpoint)
    {
        var root = string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint.Trim().TrimEnd('/');
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("OpenRouter requires an absolute HTTP endpoint.");
        return root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? root : root + "/v1";
    }

    public static IReadOnlyList<ModelCatalogEntry> Read(string json)
    {
        var data = OpenRouterJson.Read<OpenRouterData<OpenRouterModel[]>>(json).Data;
        var result = new List<ModelCatalogEntry>();
        foreach (var item in data)
        {
            var id = item.Id;
            if (string.IsNullOrWhiteSpace(id) || !item.SupportedParameters.Contains("tools")) continue;
            if (item.Architecture?.OutputModalities.Contains("text") != true) continue;
            var protocols = item.Endpoints.Select(ProtocolForPath).OfType<string>().Distinct().ToArray();
            if (protocols.Length == 0) continue;
            var reasoning = item.Reasoning;
            var supportsReasoning = item.SupportedParameters.Contains("reasoning");
            var upstreamDefault = ParseEffort(reasoning?.DefaultEffort);
            var known = supportsReasoning && (reasoning is not { HasSupportedEfforts: true, Mandatory: not null } || upstreamDefault is null)
                ? ModelThinkingAdapterCatalog.ResolveReasoningCapability("anthropic", null, id) : null;
            if (known?.Models.Count == 0) known = null;
            var efforts = (reasoning?.SupportedEfforts ?? []).Select(ParseEffort).OfType<ProviderReasoningEffort>().Distinct().OrderBy(effort => effort).ToArray();
            if (supportsReasoning && reasoning is not { HasSupportedEfforts: true })
                efforts = known?.SupportedEfforts.Select(e => e.Effort).ToArray() ?? [];
            if (supportsReasoning && reasoning is { HasSupportedEfforts: true, SupportedEfforts: null })
                efforts = [ProviderReasoningEffort.Low, ProviderReasoningEffort.Medium, ProviderReasoningEffort.High, ProviderReasoningEffort.ExtraHigh, ProviderReasoningEffort.Max];
            var defaultEffort = upstreamDefault ?? known?.DefaultEffort ?? ProviderReasoningEffort.Medium;
            if (ModelCapabilityIdentity.MatchesPrefix(ModelCapabilityIdentity.ForMatching(id.Split('/')[^1])!, "claude-opus-5-5")
                && efforts.Contains(ProviderReasoningEffort.Medium)) defaultEffort = ProviderReasoningEffort.Medium;
            if (efforts.Length > 0 && !efforts.Contains(defaultEffort)) defaultEffort = efforts[0];
            var mandatory = reasoning?.Mandatory ?? known is { SupportsDisable: false };
            var defaultEnabled = mandatory || reasoning is { DefaultEnabled: true };
            var pricing = item.Pricing;
            var model = new ModelCatalogEntry
            {
                Id = id, DisplayName = item.Name, OwnedBy = id.Split('/')[0],
                CreatedAt = item.Created is { } timestamp ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : default,
                ContextWindow = Positive(item.TopProvider?.ContextLength) ?? Positive(item.ContextLength),
                MaxOutputTokens = Positive(item.TopProvider?.MaxCompletionTokens), SupportedProtocols = protocols,
                InputModalities = item.Architecture.InputModalities,
                Reasoning = supportsReasoning ? new(!mandatory, efforts, defaultEffort, defaultEnabled, upstreamDefault) : null,
                Pricing = new(Price(pricing?.Prompt), Price(pricing?.Completion), Price(pricing?.InputCacheRead), Price(pricing?.InputCacheWrite))
            };
            model.DefaultProtocol = SelectProtocol(model, "auto");
            result.Add(model);
        }
        return result.OrderBy(model => model.Id, StringComparer.Ordinal).ToArray();
    }

    public static string SelectProtocol(ModelCatalogEntry model, string? requested)
    {
        var available = model.SupportedProtocols ?? [];
        if (!string.IsNullOrWhiteSpace(requested) && requested != "auto")
            return available.Contains(requested) ? requested : throw new ArgumentException($"Model '{model.Id}' does not support protocol '{requested}'.");
        string[] preferred = model.Id.StartsWith("anthropic/", StringComparison.Ordinal) || model.Id.StartsWith("~anthropic/", StringComparison.Ordinal)
            ? ["anthropic", "openai-chat-completions", "openai-responses"]
            : model.Id.StartsWith("openai/", StringComparison.Ordinal) || model.Id.StartsWith("~openai/", StringComparison.Ordinal)
                ? ["openai-responses", "openai-chat-completions", "anthropic"]
                : ["openai-chat-completions", "openai-responses", "anthropic"];
        return preferred.FirstOrDefault(available.Contains) ?? throw new ArgumentException($"Model '{model.Id}' has no supported protocol.");
    }

    internal static BuiltInModelCatalog.ProviderCatalog Bundled() => BuiltInModelCatalog.Instance.Providers[OpenRouterBackend.Id];

    internal static ProviderReasoningEffort? ParseEffort(string? value) => value switch
    {
        "low" or "minimal" => ProviderReasoningEffort.Low, "medium" => ProviderReasoningEffort.Medium,
        "high" => ProviderReasoningEffort.High, "xhigh" => ProviderReasoningEffort.ExtraHigh,
        "max" => ProviderReasoningEffort.Max, _ => null
    };
    internal static string? ProtocolForPath(string? path) => path?.Split('?')[0].TrimEnd('/') switch
    {
        "/api/v1/messages" => "anthropic", "/api/v1/chat/completions" => "openai-chat-completions",
        "/api/v1/responses" => "openai-responses", _ => null
    };
    private static int? Positive(int? value) => value is > 0 ? value : null;
    private static decimal? Price(decimal? value) => value is >= 0 ? value * 1_000_000 : null;
}
