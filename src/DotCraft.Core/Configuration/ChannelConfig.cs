using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

public sealed record ChannelEntry
{
    [JsonIgnore]
    public string Name { get; init; } = string.Empty;
    public string PluginId { get; init; } = string.Empty;
    public bool Enabled { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PluginActivationId { get; set; }
}

public static class ChannelEntryMap
{
    public static Dictionary<string, ChannelEntry> ToDictionaryByNameLastWins(IEnumerable<ChannelEntry> entries)
    {
        var entriesByName = new Dictionary<string, ChannelEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.PluginId))
                entriesByName[entry.Name] = entry;
        }
        return entriesByName;
    }
}

public sealed class ChannelConfigListConverter : JsonConverter<List<ChannelEntry>>
{
    public override List<ChannelEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var entries = new List<ChannelEntry>();
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.Deserialize<ChannelEntry>(options) is { } entry)
                    entries.Add(entry with { Name = property.Name });
            }
        }
        return entries;
    }

    public override void Write(Utf8JsonWriter writer, List<ChannelEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (name, entry) in ChannelEntryMap.ToDictionaryByNameLastWins(value).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(name);
            JsonSerializer.Serialize(writer, entry, options);
        }
        writer.WriteEndObject();
    }
}
