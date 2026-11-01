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
var canonicalIds = canonical.RootElement.EnumerateObject().Select(model => model.Name).ToHashSet(StringComparer.Ordinal);
var providers = await http.GetFromJsonAsync<Dictionary<string, ModelsDevProvider>>(pricingSource, options)
    ?? throw new JsonException("models.dev returned no provider catalog.");
var prices = providers.SelectMany(provider => provider.Value.Models.Select(model => new
{
    Provider = provider.Key,
    NativeId = model.Key,
    CanonicalId = model.Value.CanonicalModelId ?? provider.Key + "/" + model.Key,
    Pricing = ReadPricing(model.Value.Cost)
})).Where(model => model.CanonicalId.StartsWith(model.Provider + "/", StringComparison.Ordinal)
    && canonicalIds.Contains(model.CanonicalId) && model.Pricing is not null).ToArray();
var priced = 0;
foreach (var (id, value) in catalog["models"]!.AsObject())
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
Console.WriteLine($"Updated official base prices for {priced} existing models.");
if (!pricingOnly)
{
    const string source = "https://openrouter.ai/api/v1/models";
    var models = OpenRouterModelCatalog.Read(await http.GetStringAsync(source));
    if (models.Count == 0) throw new InvalidOperationException("OpenRouter returned no tool-capable text models.");
    var routingProviders = catalog["providers"] as JsonObject ?? new JsonObject();
    routingProviders["openrouter"] = JsonSerializer.SerializeToNode(new { source, capturedAt = DateTimeOffset.UtcNow, models }, options);
    catalog["providers"] = routingProviders;
    Console.WriteLine($"Updated {models.Count} OpenRouter models.");
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

internal sealed class ModelsDevProvider
{
    public Dictionary<string, ModelsDevModel> Models { get; init; } = [];
}

internal sealed record ModelsDevModel(
    [property: JsonPropertyName("canonical_model_id")] string? CanonicalModelId,
    ModelsDevCost? Cost);

internal sealed record ModelsDevCost(decimal? Input, decimal? Output,
    [property: JsonPropertyName("cache_read")] decimal? CacheRead,
    [property: JsonPropertyName("cache_write")] decimal? CacheWrite);
