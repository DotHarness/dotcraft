using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>
/// Configuration for a single subagent runtime profile.
/// </summary>
[ConfigSection("SubAgentProfiles", DisplayName = "SubAgent Profiles", Order = 97, RootKey = "SubAgentProfiles")]
public sealed class SubAgentProfile
{
    /// <summary>
    /// Canonical profile name. Persisted as the object key under <c>SubAgentProfiles</c>.
    /// </summary>
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Runtime type identifier, e.g. <c>native</c> or a future CLI runtime.
    /// </summary>
    [ConfigField(Hint = "Runtime type identifier, e.g. native, cli-oneshot, acp.")]
    public string Runtime { get; set; } = "native";

    /// <summary>
    /// Executable name or absolute path for CLI-backed runtimes.
    /// </summary>
    [ConfigField(Hint = "Optional for CLI runtimes. Ignored by the native runtime.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Bin { get; set; }

    /// <summary>
    /// Fixed arguments prepended to every CLI invocation.
    /// </summary>
    [ConfigField(Hint = "One argument per line in Dashboard. Ignored by the native runtime.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    /// <summary>
    /// Environment variable overrides applied to the runtime.
    /// </summary>
    [ConfigField(Hint = "Optional environment variables for CLI runtimes.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Optional environment variable names copied from the parent process when present.
    /// </summary>
    [ConfigField(Hint = "Optional environment variable names to copy from parent process.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? EnvPassthrough { get; set; }

    /// <summary>
    /// Working directory resolution mode.
    /// </summary>
    [ConfigField(FieldType = "select", Options = new[] { "workspace", "specified" })]
    public string WorkingDirectoryMode { get; set; } = "workspace";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsStreaming { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsResume { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsModelSelection { get; set; }

    [ConfigField(FieldType = "select", Options = new[] { "text", "json" })]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputFormat { get; set; }

    [ConfigField(FieldType = "select", Options = new[] { "text", "json" })]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFormat { get; set; }

    [ConfigField(FieldType = "select", Options = new[] { "stdin", "arg", "arg-template", "env" })]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputMode { get; set; }

    [ConfigField(Hint = "Template used when inputMode=arg-template. Supports {task}.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputArgTemplate { get; set; }

    [ConfigField(Hint = "Environment variable name used when inputMode=env.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InputEnvKey { get; set; }

    [ConfigField(Hint = "Template used when resuming an external CLI session. Supports {sessionId}.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeArgTemplate { get; set; }

    [ConfigField(Hint = "Dot-separated JSON path used to extract the external CLI session id from stdout.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeSessionIdJsonPath { get; set; }

    [ConfigField(Hint = "Regular expression used to extract the external CLI session id from stdout.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResumeSessionIdRegex { get; set; }

    [ConfigField(Hint = "Dot-separated JSON path to the assistant response field.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputJsonPath { get; set; }

    [ConfigField(Hint = "Dot-separated JSON path to the external CLI input token count.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputInputTokensJsonPath { get; set; }

    [ConfigField(Hint = "Dot-separated JSON path to the external CLI output token count.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputOutputTokensJsonPath { get; set; }

    [ConfigField(Hint = "Optional dot-separated JSON path to a total token count when the CLI only reports one number.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputTotalTokensJsonPath { get; set; }

    [ConfigField(Hint = "Template used to append an output file argument. Supports {path}.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputFileArgTemplate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReadOutputFile { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeleteOutputFileAfterRead { get; set; }

    [ConfigField(Min = 1, Hint = "Maximum captured output size in bytes before truncation.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxOutputBytes { get; set; }

    [ConfigField(Min = 0, Hint = "Per-task timeout in seconds. 0 = no limit.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Timeout { get; set; }

    [ConfigField(FieldType = "select", Options = new[] { "trusted", "prompt", "restricted" })]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustLevel { get; set; }

    [ConfigField(Hint = "Optional approval mode to runtime flag mapping.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PermissionModeMapping { get; set; }

    [ConfigField(FieldType = "json", Hint = "Optional runtime-specific sanitization rules.")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? SanitizationRules { get; set; }

    public SubAgentProfile Clone() =>
        new()
        {
            Name = Name,
            Runtime = Runtime,
            Bin = Bin,
            Args = Args != null ? [.. Args] : null,
            Env = Env != null ? new Dictionary<string, string>(Env, StringComparer.Ordinal) : null,
            EnvPassthrough = EnvPassthrough != null ? [.. EnvPassthrough] : null,
            WorkingDirectoryMode = WorkingDirectoryMode,
            SupportsStreaming = SupportsStreaming,
            SupportsResume = SupportsResume,
            SupportsModelSelection = SupportsModelSelection,
            InputFormat = InputFormat,
            OutputFormat = OutputFormat,
            InputMode = InputMode,
            InputArgTemplate = InputArgTemplate,
            InputEnvKey = InputEnvKey,
            ResumeArgTemplate = ResumeArgTemplate,
            ResumeSessionIdJsonPath = ResumeSessionIdJsonPath,
            ResumeSessionIdRegex = ResumeSessionIdRegex,
            OutputJsonPath = OutputJsonPath,
            OutputInputTokensJsonPath = OutputInputTokensJsonPath,
            OutputOutputTokensJsonPath = OutputOutputTokensJsonPath,
            OutputTotalTokensJsonPath = OutputTotalTokensJsonPath,
            OutputFileArgTemplate = OutputFileArgTemplate,
            ReadOutputFile = ReadOutputFile,
            DeleteOutputFileAfterRead = DeleteOutputFileAfterRead,
            MaxOutputBytes = MaxOutputBytes,
            Timeout = Timeout,
            TrustLevel = TrustLevel,
            PermissionModeMapping = PermissionModeMapping != null
                ? new Dictionary<string, string>(PermissionModeMapping, StringComparer.OrdinalIgnoreCase)
                : null,
            SanitizationRules = SanitizationRules?.DeepClone() as JsonObject
        };
}

/// <summary>
/// Maps subagent profiles by name with case-insensitive keys. Later entries win.
/// </summary>
public static class SubAgentProfileMap
{
    public static Dictionary<string, SubAgentProfile> ToDictionaryByNameLastWins(
        IEnumerable<SubAgentProfile> entries)
    {
        var result = new Dictionary<string, SubAgentProfile>(StringComparer.OrdinalIgnoreCase);
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
/// Serializes <c>SubAgentProfiles</c> as an object map keyed by profile name while exposing
/// the in-memory model as a strongly typed list.
/// </summary>
public sealed class SubAgentProfileListConverter : JsonConverter<List<SubAgentProfile>>
{
    public override List<SubAgentProfile>? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<SubAgentProfile>();

        if (root.ValueKind != JsonValueKind.Object)
            return list;

        foreach (var prop in root.EnumerateObject())
        {
            var profile = prop.Value.Deserialize<SubAgentProfile>(options) ?? new SubAgentProfile();
            profile.Name = prop.Name;
            list.Add(profile);
        }

        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<SubAgentProfile> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var profile in value.Where(p => !string.IsNullOrWhiteSpace(p.Name))
                     .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(profile.Name);
            JsonSerializer.Serialize(writer, profile, options);
        }

        writer.WriteEndObject();
    }
}
