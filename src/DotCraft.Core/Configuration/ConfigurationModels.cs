using System.Text.Json.Nodes;

namespace DotCraft.Configuration;

public enum ConfigLayerType
{
    User,
    Workspace
}

public sealed record ConfigLayerInfo(ConfigLayerType Type, string FilePath, string Version);

public sealed record ConfigLayerSnapshot(ConfigLayerInfo Layer, JsonObject Config);

public sealed record ConfigReadResult(
    JsonObject Config,
    IReadOnlyDictionary<string, ConfigLayerInfo> Origins,
    IReadOnlyList<ConfigLayerSnapshot>? Layers);

public enum ConfigMergeStrategy
{
    Replace,
    Upsert
}

public sealed record ConfigEdit(string KeyPath, JsonNode? Value, ConfigMergeStrategy MergeStrategy = ConfigMergeStrategy.Replace);

public enum ConfigWriteStatus
{
    Ok,
    OkOverridden
}

public sealed record ConfigOverriddenMetadata(string Message, ConfigLayerInfo OverridingLayer, JsonNode? EffectiveValue);

public sealed record ConfigWriteResult(
    ConfigWriteStatus Status,
    string Version,
    string FilePath,
    ConfigOverriddenMetadata? OverriddenMetadata);

public enum ConfigWriteErrorCode
{
    ConfigLayerReadonly,
    ConfigVersionConflict,
    ConfigValidationError,
    ConfigSchemaUnknownKey
}

public sealed class ConfigWriteException(ConfigWriteErrorCode code, string message) : Exception(message)
{
    public ConfigWriteErrorCode Code { get; } = code;
}
