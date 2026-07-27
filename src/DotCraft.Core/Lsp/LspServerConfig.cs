using System.Text.Json;
using System.Text.Json.Serialization;
using DotCraft.Configuration;

namespace DotCraft.Lsp;

public sealed class LspServerOrigin
{
    public string Kind { get; set; } = "workspace";

    public string? PluginId { get; set; }

    public string? PluginDisplayName { get; set; }

    public string? DeclaredName { get; set; }

    [JsonIgnore]
    public bool IsPlugin => string.Equals(Kind, "plugin", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsWorkspace => !IsPlugin;

    public static LspServerOrigin Workspace() => new() { Kind = "workspace" };

    public static LspServerOrigin Plugin(string pluginId, string? pluginDisplayName, string declaredName) =>
        new()
        {
            Kind = "plugin",
            PluginId = pluginId,
            PluginDisplayName = pluginDisplayName,
            DeclaredName = declaredName
        };

    public LspServerOrigin Clone() =>
        new()
        {
            Kind = string.IsNullOrWhiteSpace(Kind) ? "workspace" : Kind,
            PluginId = PluginId,
            PluginDisplayName = PluginDisplayName,
            DeclaredName = DeclaredName
        };
}

[ConfigSection("LspServers", DisplayName = "LSP Servers", Order = 96, RootKey = "LspServers")]
public sealed class LspServerConfig
{
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public string Command { get; set; } = string.Empty;

    [ConfigField(Hint = "One argument per line in Dashboard.")]
    public List<string> Arguments { get; set; } = [];

    [ConfigField(Hint = "Map file extension to language id, e.g. {\".cs\":\"csharp\"}")]
    public Dictionary<string, string> ExtensionToLanguage { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [ConfigField(FieldType = "select", Options = ["stdio", "socket"])]
    public string Transport { get; set; } = "stdio";

    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    [ConfigField(FieldType = "json", Hint = "JSON object passed as LSP initialize.initializationOptions")]
    public JsonElement? InitializationOptions { get; set; }

    [ConfigField(FieldType = "json", Hint = "JSON object for workspace settings (reserved for future use)")]
    public JsonElement? Settings { get; set; }

    [ConfigField(Hint = "Optional workspace folder path for this server")]
    public string? WorkspaceFolder { get; set; }

    [ConfigField(Min = 1, Hint = "milliseconds")]
    public int? StartupTimeoutMs { get; set; }

    [ConfigField(Min = 0, Hint = "Maximum restart attempts when server crashes")]
    public int? MaxRestarts { get; set; }

    [JsonIgnore]
    public LspServerOrigin Origin { get; set; } = LspServerOrigin.Workspace();

    [JsonIgnore]
    public bool ReadOnly => Origin.IsPlugin;

    [JsonIgnore]
    public string NormalizedTransport =>
        Transport.Equals("socket", StringComparison.OrdinalIgnoreCase)
            ? "socket"
            : "stdio";

    public LspServerConfig Clone() =>
        new()
        {
            Name = Name,
            Enabled = Enabled,
            Command = Command,
            Arguments = [.. Arguments],
            ExtensionToLanguage = new Dictionary<string, string>(ExtensionToLanguage, StringComparer.OrdinalIgnoreCase),
            Transport = Transport,
            EnvironmentVariables = new Dictionary<string, string>(EnvironmentVariables, StringComparer.Ordinal),
            InitializationOptions = InitializationOptions,
            Settings = Settings,
            WorkspaceFolder = WorkspaceFolder,
            StartupTimeoutMs = StartupTimeoutMs,
            MaxRestarts = MaxRestarts,
            Origin = Origin.Clone()
        };
}

/// <summary>
/// Reads and writes the canonical object-map form:
/// { "LspServers": { "serverName": { ... } } }.
/// </summary>
public sealed class LspServerConfigListConverter : JsonConverter<List<LspServerConfig>>
{
    public override List<LspServerConfig>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<LspServerConfig>();

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("LspServers must be an object keyed by server name.");

        foreach (var prop in root.EnumerateObject())
        {
            var cfg = DeserializeConfig(prop.Value, options) ?? new LspServerConfig();
            if (string.IsNullOrWhiteSpace(cfg.Name))
                cfg.Name = prop.Name;
            list.Add(cfg);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<LspServerConfig> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var server in value.Where(s => !string.IsNullOrWhiteSpace(s.Name))
                     .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(server.Name);
            JsonSerializer.Serialize(writer, server, options);
        }

        writer.WriteEndObject();
    }

    private static LspServerConfig? DeserializeConfig(JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException("Each LSP server entry must be an object.");

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals("args", StringComparison.OrdinalIgnoreCase)
                || property.Name.Equals("env", StringComparison.OrdinalIgnoreCase))
                throw new JsonException(
                    $"LSP server property '{property.Name}' is no longer supported. Use arguments or environmentVariables.");
        }

        var cfg = element.Deserialize<LspServerConfig>(options);
        if (cfg == null)
            return null;

        if (string.IsNullOrWhiteSpace(cfg.Name)
            && TryGetPropertyIgnoreCase(element, "name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String)
        {
            cfg.Name = nameElement.GetString() ?? string.Empty;
        }

        return cfg;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (prop.NameEquals(propertyName) || prop.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
