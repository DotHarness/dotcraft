#:project ../src/DotCraft.Agents.OpenRouter/DotCraft.Agents.OpenRouter.csproj
#:property PublishAot=false

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Configuration;

var pricingOnly = args.Contains("--pricing-only");
var path = args.FirstOrDefault(arg => arg != "--pricing-only") ?? "src/DotCraft.Agents/Resources/models.json";
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter<ProviderReasoningEffort>(JsonNamingPolicy.CamelCase) }
};
using var http = new HttpClient();
var catalog = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
const string pricingSource = "https://models.dev/api.json";
using var canonical = JsonDocument.Parse(await http.GetStringAsync("https://models.dev/models.json"));
var canonicalModels = canonical.RootElement;
var canonicalIds = canonicalModels.EnumerateObject().Select(model => model.Name).ToHashSet(StringComparer.Ordinal);
var providers = await http.GetFromJsonAsync<Dictionary<string, ModelsDevProvider>>(pricingSource, options)
    ?? throw new JsonException("models.dev returned no provider catalog.");
var officialModels = providers.SelectMany(provider => provider.Value.Models.Select(model => new OfficialModel(
    provider.Key, model.Key, model.Value.CanonicalModelId ?? provider.Key + "/" + model.Key,
    model.Value.Limit, ReadPricing(model.Value.Cost))))
    .Where(model => model.CanonicalId.StartsWith(model.Provider + "/", StringComparison.Ordinal)
        && canonicalIds.Contains(model.CanonicalId)).ToArray();
if (officialModels.Length == 0)
    throw new InvalidOperationException("models.dev returned no first-party records matching the canonical catalog.");
var prices = officialModels.Where(model => model.Pricing is not null).ToArray();
var models = catalog["models"]!.AsObject();
if (!pricingOnly)
{
    models = SynchronizeModels(models, canonicalModels, officialModels);
    catalog["models"] = models;
}
var priced = 0;
foreach (var (id, value) in models)
{
    var entry = value!.AsObject();
    var key = ModelCapabilityIdentity.ForMatching(id)!;
    var exact = prices.Where(model => Key(model.NativeId).Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
    var matches = exact.Length > 0 ? exact : prices.Where(model => Key(model.CanonicalId).Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray();
    var sources = matches.Select(model => model.Provider).Distinct().ToArray();
    var rates = matches.Select(model => model.Pricing).Distinct().ToArray();
    entry.Remove("pricing");
    entry.Remove("pricingProvider");
    if (sources.Length != 1 || rates.Length != 1) continue;
    entry["pricingProvider"] = sources[0];
    entry["pricing"] = JsonSerializer.SerializeToNode(rates[0], options);
    priced++;
}
catalog["pricingSource"] = pricingSource;
catalog["pricingCapturedAt"] = DateTimeOffset.UtcNow;
Console.WriteLine($"Updated official base prices for {priced} models.");
if (!pricingOnly)
{
    const string source = "https://openrouter.ai/api/v1/models";
    var openRouterModels = OpenRouterModelCatalog.Read(await http.GetStringAsync(source));
    if (openRouterModels.Count == 0) throw new InvalidOperationException("OpenRouter returned no tool-capable text models.");
    var routingProviders = catalog["providers"] as JsonObject ?? new JsonObject();
    routingProviders["openrouter"] = JsonSerializer.SerializeToNode(new { source, capturedAt = DateTimeOffset.UtcNow, models = openRouterModels }, options);
    catalog["providers"] = routingProviders;
    Console.WriteLine($"Updated {openRouterModels.Count} OpenRouter models.");
}
await File.WriteAllTextAsync(path, catalog.ToJsonString(options) + "\n");

static string Key(string id) => ModelCapabilityIdentity.ForMatching(id.Split('/')[^1])!;

static ModelPricing? ReadPricing(ModelsDevCost? cost)
{
    if (cost is null) return null;
    var price = new ModelPricing(Rate(cost.Input), Rate(cost.Output), Rate(cost.CacheRead), Rate(cost.CacheWrite));
    return price is { InputPerMillion: null, OutputPerMillion: null, CacheReadPerMillion: null, CacheWritePerMillion: null } ? null : price;
}

static decimal? Rate(decimal? value) => value is >= 0 ? value : null;

static JsonObject SynchronizeModels(JsonObject previous, JsonElement canonical, OfficialModel[] officialModels)
{
    const int minContextWindow = 1_000;
    var contexts = new Dictionary<string, int>(StringComparer.Ordinal);
    var defaults = new SortedDictionary<string, (int Context, int? Input)>(StringComparer.Ordinal);
    var officialByCanonical = officialModels.ToLookup(model => model.CanonicalId, StringComparer.Ordinal);
    foreach (var model in canonical.EnumerateObject())
    {
        if (model.Value.ValueKind != JsonValueKind.Object)
            continue;
        if (!model.Value.TryGetProperty("tool_call", out var toolCall) || toolCall.ValueKind != JsonValueKind.True)
            continue;
        if (!model.Value.TryGetProperty("modalities", out var modalities)
            || !modalities.TryGetProperty("output", out var outputs)
            || !outputs.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && item.GetString() == "text"))
            continue;
        if (!model.Value.TryGetProperty("limit", out var limit)
            || !TryReadPositive(limit, "context", out var context) || context < minContextWindow)
            continue;

        contexts.Add(model.Name, context);
        var inputLimits = officialByCanonical[model.Name].Select(record => record.Limit?.Input)
            .OfType<int>().Where(input => input > 0).Distinct().ToArray();
        int? maxInput = inputLimits is [var input] && input < context ? input : null;
        var key = Key(model.Name).ToLowerInvariant();
        if (!defaults.TryAdd(key, (context, maxInput)))
            throw new InvalidOperationException($"Duplicate normalized canonical model key '{key}'.");
    }

    if (contexts.Count == 0)
        throw new InvalidOperationException("models.dev returned no eligible canonical models.");

    var directModels = officialModels.Where(model => contexts.ContainsKey(model.CanonicalId))
        .GroupBy(model => Key(model.NativeId).ToLowerInvariant(), StringComparer.Ordinal);
    foreach (var group in directModels)
    {
        if (defaults.ContainsKey(group.Key)) continue;
        var records = group.Select(model =>
        {
            var context = model.Limit?.Context is int limit && limit >= minContextWindow
                ? limit : contexts[model.CanonicalId];
            var input = model.Limit?.Input;
            return (model.CanonicalId, Context: context, Input: input is > 0 && input < context ? input : null);
        }).Distinct().ToArray();
        if (records is not [var record])
            throw new InvalidOperationException($"Conflicting first-party records for model key '{group.Key}'.");
        defaults.Add(group.Key, (record.Context, record.Input));
    }

    var models = new JsonObject();
    foreach (var (key, (context, input)) in defaults)
    {
        var entry = previous[key]?.DeepClone().AsObject() ?? new JsonObject();
        entry["contextWindow"] = context;
        if (input is { } limit) entry["maxInputTokens"] = limit;
        else entry.Remove("maxInputTokens");
        models.Add(key, entry);
    }
    Console.WriteLine($"Synchronized {contexts.Count} canonical models and {models.Count - contexts.Count} additional first-party model IDs.");
    return models;
}

static bool TryReadPositive(JsonElement element, string propertyName, out int value)
{
    value = 0;
    return element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out value)
        && value > 0;
}

internal sealed class ModelsDevProvider
{
    public Dictionary<string, ModelsDevModel> Models { get; init; } = [];
}

internal sealed record ModelsDevModel(
    [property: JsonPropertyName("canonical_model_id")] string? CanonicalModelId,
    ModelsDevCost? Cost,
    ModelsDevLimit? Limit);

internal sealed record OfficialModel(string Provider, string NativeId, string CanonicalId, ModelsDevLimit? Limit, ModelPricing? Pricing);

internal sealed record ModelsDevLimit(int? Context, int? Input);

internal sealed record ModelsDevCost(decimal? Input, decimal? Output,
    [property: JsonPropertyName("cache_read")] decimal? CacheRead,
    [property: JsonPropertyName("cache_write")] decimal? CacheWrite);
