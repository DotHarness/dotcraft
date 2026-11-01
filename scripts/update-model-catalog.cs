#:project ../src/DotCraft.Agents.OpenRouter/DotCraft.Agents.OpenRouter.csproj
#:property PublishAot=false

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Agents;
using DotCraft.Configuration;

var path = args.FirstOrDefault() ?? "src/DotCraft.Agents/Resources/models.json";
const string source = "https://openrouter.ai/api/v1/models";
using var http = new HttpClient();
var models = OpenRouterModelCatalog.Read(await http.GetStringAsync(source));
if (models.Count == 0) throw new InvalidOperationException("OpenRouter returned no tool-capable text models.");
var catalog = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
var providers = catalog["providers"] as JsonObject ?? new JsonObject();
providers["openrouter"] = JsonSerializer.SerializeToNode(new { source, capturedAt = DateTimeOffset.UtcNow, models },
    new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<ProviderReasoningEffort>(JsonNamingPolicy.CamelCase) }
    });
catalog["providers"] = providers;
await File.WriteAllTextAsync(path, catalog.ToJsonString() + "\n");
Console.WriteLine($"Updated {models.Count} OpenRouter models in {path}.");
