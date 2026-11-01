using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Agents;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class OpenRouterRoutingPreferences
{
    public string[]? Order { get; init; }
    public string[]? Only { get; init; }
    public string[]? Ignore { get; init; }
    public OpenRouterSort? Sort { get; init; }
    public bool? AllowFallbacks { get; init; }
    public bool? RequireParameters { get; init; }

    [JsonIgnore] internal bool IsEmpty => Order is null && Only is null && Ignore is null
        && Sort is null && AllowFallbacks is null && RequireParameters is null;

    internal static OpenRouterRoutingPreferences Read(string json)
    {
        OpenRouterRoutingPreferences settings;
        try { settings = OpenRouterJson.Read<OpenRouterRoutingPreferences>(json); }
        catch (JsonException ex) { throw new ArgumentException("Invalid OpenRouter routing settings.", ex); }
        foreach (var providers in new[] { settings.Order, settings.Only, settings.Ignore })
        {
            if (providers?.Any(string.IsNullOrWhiteSpace) == true)
                throw new ArgumentException("OpenRouter provider slugs must not be empty.");
        }
        return settings;
    }
}

internal enum OpenRouterSort { Price, Latency, Throughput }
