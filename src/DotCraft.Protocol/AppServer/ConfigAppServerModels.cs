using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class ConfigReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("includeLayers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> IncludeLayers { get; init; }
}

public sealed class ConfigReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("config")] public required JsonElement Config { get; init; }

    [JsonPropertyName("origins")] public required IReadOnlyDictionary<string, ConfigLayerMetadata> Origins { get; init; }

    [JsonPropertyName("layers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ConfigLayer>> Layers { get; init; }
}

public sealed class ConfigLayerName : ExtensibleJsonObject
{
    /// <summary><c>user</c> or <c>workspace</c>.</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }

    [JsonPropertyName("file")] public required string File { get; init; }
}

public sealed class ConfigLayerMetadata : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required ConfigLayerName Name { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }
}

public sealed class ConfigLayer : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required ConfigLayerName Name { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("config")] public required JsonElement Config { get; init; }
}

public sealed class ConfigEdit : ExtensibleJsonObject
{
    [JsonPropertyName("keyPath")] public required string KeyPath { get; init; }

    /// <summary>A <c>null</c> value removes the key from the target layer.</summary>
    [JsonPropertyName("value")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? Value { get; init; }

    /// <summary><c>replace</c> or <c>upsert</c>.</summary>
    [JsonPropertyName("mergeStrategy")] public required string MergeStrategy { get; init; }
}

public sealed class ConfigValueWriteParams : ExtensibleJsonObject
{
    [JsonPropertyName("keyPath")] public required string KeyPath { get; init; }

    /// <summary>A <c>null</c> value removes the key from the target layer.</summary>
    [JsonPropertyName("value")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? Value { get; init; }

    /// <summary><c>replace</c> or <c>upsert</c>.</summary>
    [JsonPropertyName("mergeStrategy")] public required string MergeStrategy { get; init; }

    /// <summary>A layer file from <c>config/read</c>; <c>null</c> targets the workspace layer.</summary>
    [JsonPropertyName("filePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilePath { get; init; }

    [JsonPropertyName("expectedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedVersion { get; init; }
}

public sealed class ConfigBatchWriteParams : ExtensibleJsonObject
{
    [JsonPropertyName("edits")] public required IReadOnlyList<ConfigEdit> Edits { get; init; }

    /// <summary>A layer file from <c>config/read</c>; <c>null</c> targets the workspace layer.</summary>
    [JsonPropertyName("filePath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FilePath { get; init; }

    [JsonPropertyName("expectedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpectedVersion { get; init; }
}

public sealed class ConfigWriteResult : ExtensibleJsonObject
{
    /// <summary><c>ok</c> or <c>okOverridden</c>.</summary>
    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("filePath")] public required string FilePath { get; init; }

    [JsonPropertyName("overriddenMetadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConfigOverriddenMetadata? OverriddenMetadata { get; init; }
}

public sealed class ConfigOverriddenMetadata : ExtensibleJsonObject
{
    [JsonPropertyName("message")] public required string Message { get; init; }

    [JsonPropertyName("overridingLayer")] public required ConfigLayerMetadata OverridingLayer { get; init; }

    [JsonPropertyName("effectiveValue")]
    [JsonRequired]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public JsonElement? EffectiveValue { get; init; }
}

public sealed class ConfigChangedParams : ExtensibleJsonObject
{
    [JsonPropertyName("changedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<DateTimeOffset> ChangedAt { get; init; }

    [JsonPropertyName("regions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<string>> Regions { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<string> Source { get; init; }
}

public sealed class ConfigSchemaParams : ExtensibleJsonObject
{
}

public sealed class ConfigSchemaResult : ExtensibleJsonObject
{
    [JsonPropertyName("sections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<IReadOnlyList<ConfigSchemaSection>> Sections { get; init; }
}
