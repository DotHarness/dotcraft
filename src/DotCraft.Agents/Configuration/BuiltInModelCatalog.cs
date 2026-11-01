using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Agents;

namespace DotCraft.Configuration;

internal sealed class BuiltInModelCatalog
{
    internal static BuiltInModelCatalog Instance { get; } = Load();

    public int DefaultContextWindow { get; init; } = 256_000;
    public Dictionary<string, ModelDefaults> Models { get; init; } = [];
    public Dictionary<string, ProviderCatalog> Providers { get; init; } = [];

    private static BuiltInModelCatalog Load()
    {
        using var stream = typeof(BuiltInModelCatalog).Assembly.GetManifestResourceStream("DotCraft.Resources.models.json")!;
        return JsonSerializer.Deserialize<BuiltInModelCatalog>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter<ProviderReasoningEffort>(JsonNamingPolicy.CamelCase) }
        })!;
    }

    internal sealed class ModelDefaults
    {
        private FastCapability? _fast;
        public int? ContextWindow { get; init; }
        public int? MaxInputTokens { get; init; }
        public FastCapability? Fast
        {
            get => _fast;
            init { _fast = value; HasFastDeclaration = true; }
        }
        [JsonIgnore] public bool HasFastDeclaration { get; private set; }
    }
    internal sealed record FastCapability(IReadOnlyList<string> Protocols);
    internal sealed record ProviderCatalog(string Source, DateTimeOffset CapturedAt, IReadOnlyList<ModelCatalogEntry> Models);
}
