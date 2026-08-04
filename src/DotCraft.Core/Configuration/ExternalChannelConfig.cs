using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>
/// Transport mode for an external channel adapter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExternalChannelTransport>))]
public enum ExternalChannelTransport
{
    /// <summary>DotCraft spawns the adapter as a child process (stdio JSONL).</summary>
    Subprocess,

    /// <summary>The adapter connects to DotCraft's AppServer WebSocket endpoint.</summary>
    Websocket,

    /// <summary>DotCraft spawns the adapter as a child process and the adapter connects back over WebSocket.</summary>
    ManagedWebsocket
}

/// <summary>
/// Configuration for a single external channel adapter.
/// </summary>
[ConfigSection("ExternalChannels", DisplayName = "External Channels", Order = 250, RootKey = "ExternalChannels")]
public sealed class ExternalChannelEntry
{
    /// <summary>
    /// Canonical channel name. Persisted as the object key under <c>ExternalChannels</c>.
    /// </summary>
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this channel is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Transport mode: "subprocess", "websocket", or "managedWebsocket".</summary>
    [ConfigField(FieldType = "select", Options = new[] { "subprocess", "websocket", "managedWebsocket" })]
    public ExternalChannelTransport Transport { get; set; } = ExternalChannelTransport.Subprocess;

    /// <summary>
    /// Command to start the adapter process. Required for subprocess mode unless <see cref="BuiltinModule"/> is set.
    /// </summary>
    [ConfigField(Hint = "Required for subprocess transport unless BuiltinModule is set.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    /// <summary>
    /// Built-in TypeScript module directory name. When set, DotCraft expands the
    /// command from Hub-provided runtime hints instead of persisting absolute paths.
    /// </summary>
    [ConfigField(Hint = "Optional built-in TypeScript module id, such as channel-telegram.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuiltinModule { get; set; }

    /// <summary>
    /// Additional command-line arguments for the subprocess.
    /// </summary>
    [ConfigField(Hint = "One argument per line in Dashboard.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    /// <summary>
    /// Working directory for the subprocess. Defaults to workspace root.
    /// </summary>
    [ConfigField(Hint = "Optional. Empty = workspace root.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Additional environment variables passed to the subprocess.
    /// </summary>
    [ConfigField(Hint = "Optional environment variables for subprocess transport.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }

    public ExternalChannelEntry Clone() =>
        new()
        {
            Name = Name,
            Enabled = Enabled,
            Transport = Transport,
            Command = Command,
            BuiltinModule = BuiltinModule,
            Args = Args != null ? [.. Args] : null,
            WorkingDirectory = WorkingDirectory,
            Env = Env != null ? new Dictionary<string, string>(Env, StringComparer.Ordinal) : null
        };
}

/// <summary>
/// Maps external channel entries by name with case-insensitive keys. When JSON contains keys that
/// differ only by case, the last entry in source order wins (same semantics as legacy <c>GetChannels()</c>).
/// <c>ToDictionary(..., StringComparer.OrdinalIgnoreCase)</c> throws on such duplicate keys in a list.
/// </summary>
public static class ExternalChannelEntryMap
{
    /// <summary>
    /// Builds a dictionary keyed by <see cref="ExternalChannelEntry.Name"/>, skipping blank names.
    /// Later entries overwrite earlier ones for the same case-insensitive name.
    /// </summary>
    public static Dictionary<string, ExternalChannelEntry> ToDictionaryByNameLastWins(
        IEnumerable<ExternalChannelEntry> entries)
    {
        var result = new Dictionary<string, ExternalChannelEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
                continue;
            result[entry.Name] = entry;
        }

        return result;
    }
}

/// <summary>
/// Serializes <c>ExternalChannels</c> as an object dictionary keyed by channel name while exposing
/// the in-memory model as a strongly typed list.
/// </summary>
public sealed class ExternalChannelConfigListConverter : JsonConverter<List<ExternalChannelEntry>>
{
    public override List<ExternalChannelEntry>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<ExternalChannelEntry>();

        if (root.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var prop in root.EnumerateObject())
        {
            var entry = prop.Value.Deserialize<ExternalChannelEntry>(options) ?? new ExternalChannelEntry();
            entry.Name = prop.Name;
            list.Add(entry);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<ExternalChannelEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var channel in value.Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(channel.Name);
            JsonSerializer.Serialize(writer, channel, options);
        }

        writer.WriteEndObject();
    }
}
