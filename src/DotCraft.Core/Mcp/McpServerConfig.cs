using DotCraft.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Mcp;

public sealed class McpServerOrigin
{
    public string Kind { get; set; } = "workspace";

    public string? PluginId { get; set; }

    public string? PluginDisplayName { get; set; }

    public string? DeclaredName { get; set; }

    [JsonIgnore]
    public bool IsPlugin => string.Equals(Kind, "plugin", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsWorkspace => !IsPlugin;

    public static McpServerOrigin Workspace() => new() { Kind = "workspace" };

    public static McpServerOrigin Plugin(string pluginId, string? pluginDisplayName, string declaredName) =>
        new()
        {
            Kind = "plugin",
            PluginId = pluginId,
            PluginDisplayName = pluginDisplayName,
            DeclaredName = declaredName
        };

    public McpServerOrigin Clone() =>
        new()
        {
            Kind = string.IsNullOrWhiteSpace(Kind) ? "workspace" : Kind,
            PluginId = PluginId,
            PluginDisplayName = PluginDisplayName,
            DeclaredName = DeclaredName
        };
}

[ConfigSection(
    "McpServers",
    DisplayName = "MCP Servers",
    Order = 95,
    RootKey = "McpServers",
    DefaultReload = ReloadBehavior.Hot,
    HasDefaultReload = true)]
public sealed class McpServerConfig
{
    private McpServerOrigin _origin = McpServerOrigin.Workspace();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Transport type: "stdio" (default) or "http".
    /// </summary>
    [ConfigField(FieldType = "select", Options = new[] { "stdio", "http" })]
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Command to launch (stdio transport only).
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Arguments for the command (stdio transport only).
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>
    /// Wire alias for <see cref="Arguments"/>.
    /// </summary>
    [JsonIgnore]
    public List<string> Args
    {
        get => Arguments;
        set => Arguments = value ?? [];
    }

    /// <summary>
    /// Environment variables for the command (stdio transport only).
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Wire alias for <see cref="EnvironmentVariables"/>.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> Env
    {
        get => EnvironmentVariables;
        set => EnvironmentVariables = value ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Environment variable names to forward from the host process (stdio transport only).
    /// </summary>
    public List<string> EnvVars { get; set; } = [];

    /// <summary>
    /// Optional working directory for stdio transport.
    /// </summary>
    public string? Cwd { get; set; }

    /// <summary>
    /// Server URL (http transport only), e.g. "https://mcp.exa.ai/mcp".
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Additional HTTP headers (http transport only).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Wire alias for <see cref="Headers"/>.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> HttpHeaders
    {
        get => Headers;
        set => Headers = value ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// HTTP headers whose values are sourced from environment variables (HTTP transport only).
    /// Key = header name, value = env var name.
    /// </summary>
    public Dictionary<string, string> EnvHttpHeaders { get; set; } = new();

    /// <summary>
    /// Bearer token env var name for HTTP transport.
    /// </summary>
    public string? BearerTokenEnvVar { get; set; }

    /// <summary>
    /// Startup timeout in seconds.
    /// </summary>
    public double? StartupTimeoutSec { get; set; }

    /// <summary>
    /// Default tool timeout in seconds.
    /// </summary>
    public double? ToolTimeoutSec { get; set; }

    [JsonIgnore]
    public McpServerOrigin Origin
    {
        get => _origin;
        set => _origin = value ?? McpServerOrigin.Workspace();
    }

    [JsonIgnore]
    public bool ReadOnly => Origin.IsPlugin;

    [JsonIgnore]
    public string NormalizedTransport =>
        Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) ||
        Transport.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) ||
        Transport.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? "streamableHttp"
            : "stdio";

    public McpServerConfig Clone() =>
        new()
        {
            Name = Name,
            Enabled = Enabled,
            Transport = Transport,
            Command = Command,
            Arguments = [.. Arguments],
            EnvironmentVariables = new Dictionary<string, string>(EnvironmentVariables, StringComparer.Ordinal),
            EnvVars = [.. EnvVars],
            Cwd = Cwd,
            Url = Url,
            Headers = new Dictionary<string, string>(Headers, StringComparer.Ordinal),
            EnvHttpHeaders = new Dictionary<string, string>(EnvHttpHeaders, StringComparer.Ordinal),
            BearerTokenEnvVar = BearerTokenEnvVar,
            StartupTimeoutSec = StartupTimeoutSec,
            ToolTimeoutSec = ToolTimeoutSec,
            Origin = Origin.Clone()
        };
}

/// <summary>
/// Supports both legacy array-form MCP config and the new object-map form:
/// { "McpServers": { "name": { ... } } }.
/// </summary>
public sealed class McpServerConfigListConverter : JsonConverter<List<McpServerConfig>>
{
    public override List<McpServerConfig>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<McpServerConfig>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var cfg = item.Deserialize<McpServerConfig>(options);
                if (cfg != null)
                {
                    ApplyWireAliases(cfg, item, options);
                    list.Add(cfg);
                }
            }
            return list;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var cfg = prop.Value.Deserialize<McpServerConfig>(options) ?? new McpServerConfig();
                ApplyWireAliases(cfg, prop.Value, options);
                if (string.IsNullOrWhiteSpace(cfg.Name))
                    cfg.Name = prop.Name;
                list.Add(cfg);
            }
            return list;
        }

        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<McpServerConfig> value, JsonSerializerOptions options)
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

    private static void ApplyWireAliases(McpServerConfig cfg, JsonElement element, JsonSerializerOptions options)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (!HasProperty(element, "arguments")
            && cfg.Arguments.Count == 0
            && TryGetProperty(element, "args", out var argsElement))
        {
            cfg.Arguments = argsElement.Deserialize<List<string>>(options) ?? [];
        }

        if (!HasProperty(element, "environmentVariables")
            && cfg.EnvironmentVariables.Count == 0
            && TryGetProperty(element, "env", out var envElement))
        {
            cfg.EnvironmentVariables = envElement.Deserialize<Dictionary<string, string>>(options)
                                       ?? new Dictionary<string, string>();
        }

        if (!HasProperty(element, "headers")
            && cfg.Headers.Count == 0
            && TryGetProperty(element, "httpHeaders", out var headersElement))
        {
            cfg.Headers = headersElement.Deserialize<Dictionary<string, string>>(options)
                          ?? new Dictionary<string, string>();
        }
    }

    private static bool HasProperty(JsonElement element, string name) =>
        TryGetProperty(element, name, out _);

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(name) || string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
