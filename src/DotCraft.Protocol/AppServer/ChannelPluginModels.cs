using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class PluginChannelInfo : ExtensibleJsonObject
{
    [JsonPropertyName("moduleId")] public required string ModuleId { get; init; }

    [JsonPropertyName("channelName")] public required string ChannelName { get; init; }

    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    [JsonPropertyName("localizedDisplayName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? LocalizedDisplayName { get; init; }

    [JsonPropertyName("interface")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Interface { get; init; }

    [JsonPropertyName("packageName")] public required string PackageName { get; init; }

    [JsonPropertyName("variant")] public required string Variant { get; init; }

    [JsonPropertyName("requiresInteractiveSetup")] public required bool RequiresInteractiveSetup { get; init; }

    [JsonPropertyName("capabilitySummary")] public required PluginChannelCapabilitySummary CapabilitySummary { get; init; }

    [JsonPropertyName("supportedTransports")] public required IReadOnlyList<string> SupportedTransports { get; init; }

    [JsonPropertyName("channelContractVersion")] public required string ChannelContractVersion { get; init; }

    [JsonPropertyName("supportedChannelProtocolVersions")]
    public required IReadOnlyList<string> SupportedChannelProtocolVersions { get; init; }

    [JsonPropertyName("minHostVersion")] public required string MinHostVersion { get; init; }

    [JsonPropertyName("compatibilityError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompatibilityError { get; init; }

    [JsonPropertyName("configDescriptors")] public required IReadOnlyList<JsonElement> ConfigDescriptors { get; init; }

    [JsonPropertyName("configGroups")] public required IReadOnlyList<JsonElement> ConfigGroups { get; init; }
}

public sealed class PluginChannelCapabilitySummary : ExtensibleJsonObject
{
    [JsonPropertyName("hasChannelTools")] public required bool HasChannelTools { get; init; }

    [JsonPropertyName("requiresInteractiveSetup")] public required bool RequiresInteractiveSetup { get; init; }

    [JsonPropertyName("capabilitySetMayVaryByEnvironment")]
    public required bool CapabilitySetMayVaryByEnvironment { get; init; }
}

public sealed class ChannelSetEnabledParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("pluginId")] public required string PluginId { get; init; }

    [JsonPropertyName("enabled")] public required bool Enabled { get; init; }

    [JsonPropertyName("select")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Select { get; init; }
}

public sealed class ChannelLogsParams : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("tail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Tail { get; init; }
}

public sealed class ChannelLogsResult : ExtensibleJsonObject
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("lines")] public required IReadOnlyList<string> Lines { get; init; }
}

public sealed class ChannelConfigOptionAvailabilityParams : ExtensibleJsonObject
{
    [JsonPropertyName("key")] public required string Key { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}

public sealed class ChannelConfigOptionAvailabilityResult : ExtensibleJsonObject
{
    [JsonPropertyName("available")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required IReadOnlyList<string>? Available { get; init; }

    [JsonPropertyName("key")] public required string Key { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}

public sealed class ChannelSetupReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("pluginId")] public required string PluginId { get; init; }
}

public sealed class ChannelSetupReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("artifact")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required ChannelSetupArtifact? Artifact { get; init; }
}

public sealed class ChannelSetupArtifact : ExtensibleJsonObject
{
    [JsonPropertyName("revision")] public required string Revision { get; init; }

    [JsonPropertyName("mimeType")] public required string MimeType { get; init; }

    [JsonPropertyName("data")] public required string Data { get; init; }
}
