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

    public string? ThreadId { get; set; }

    public string? BindingId { get; set; }

    [JsonIgnore]
    public bool IsPlugin => string.Equals(Kind, "plugin", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsThread => string.Equals(Kind, "thread", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsBinding => string.Equals(Kind, "binding", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsWorkspace => string.Equals(Kind, "workspace", StringComparison.OrdinalIgnoreCase);

    public static McpServerOrigin Workspace() => new() { Kind = "workspace" };

    public static McpServerOrigin Plugin(string pluginId, string? pluginDisplayName, string declaredName) =>
        new()
        {
            Kind = "plugin",
            PluginId = pluginId,
            PluginDisplayName = pluginDisplayName,
            DeclaredName = declaredName
        };

    public static McpServerOrigin Thread(string threadId, string declaredName) =>
        new() { Kind = "thread", ThreadId = threadId, DeclaredName = declaredName };

    public static McpServerOrigin Binding(string bindingId, string declaredName) =>
        new() { Kind = "binding", BindingId = bindingId, DeclaredName = declaredName };

    public McpServerOrigin Clone() =>
        new()
        {
            Kind = string.IsNullOrWhiteSpace(Kind) ? "workspace" : Kind,
            PluginId = PluginId,
            PluginDisplayName = PluginDisplayName,
            DeclaredName = DeclaredName,
            ThreadId = ThreadId,
            BindingId = BindingId
        };
}

[ConfigSection(
    "McpServers",
    DisplayName = "MCP Servers",
    Order = 95,
    RootKey = "McpServers",
    DefaultReload = ReloadBehavior.Hot,
    HasDefaultReload = true)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
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
    /// Environment variables for the command (stdio transport only).
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

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
    public bool ReadOnly => !Origin.IsWorkspace;

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
/// Reads and writes the canonical object-map MCP config:
/// { "McpServers": { "name": { ... } } }.
/// </summary>
public sealed class McpServerConfigListConverter : JsonConverter<List<McpServerConfig>>
{
    public override List<McpServerConfig>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<McpServerConfig>();

        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException("McpServers must be an object keyed by server name.");

        foreach (var prop in root.EnumerateObject())
        {
            var cfg = prop.Value.Deserialize<McpServerConfig>(options) ?? new McpServerConfig();
            if (string.IsNullOrWhiteSpace(cfg.Name))
                cfg.Name = prop.Name;
            list.Add(cfg);
        }

        return list;
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
}
