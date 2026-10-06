using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class AgentPackageRef : ExtensibleJsonObject
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }
}

public sealed class AgentExportPlanParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("source")] public required string Source { get; init; }
}

public sealed class AgentExportPackage : ExtensibleJsonObject
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("dotnet")] public required bool Dotnet { get; init; }

    [JsonPropertyName("bytes")] public required int Bytes { get; init; }

    [JsonPropertyName("marketplaceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarketplaceName { get; init; }

    [JsonPropertyName("reasons")] public required IReadOnlyList<string> Reasons { get; init; }
}

public sealed class AgentExportPlanResult : ExtensibleJsonObject
{
    [JsonPropertyName("fileName")] public required string FileName { get; init; }

    [JsonPropertyName("maximumBytes")] public required int MaximumBytes { get; init; }

    [JsonPropertyName("packages")] public required IReadOnlyList<AgentExportPackage> Packages { get; init; }
}

public sealed class AgentExportReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("source")] public required string Source { get; init; }

    [JsonPropertyName("packages")] public required IReadOnlyList<AgentPackageRef> Packages { get; init; }

    [JsonPropertyName("offset")] public required int Offset { get; init; }
}

public sealed class AgentExportReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("totalBytes")] public required int TotalBytes { get; init; }

    [JsonPropertyName("dataBase64")] public required string DataBase64 { get; init; }
}

public sealed class AgentImportUploadParams : ExtensibleJsonObject
{
    [JsonPropertyName("importId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImportId { get; init; }

    [JsonPropertyName("fileName")] public required string FileName { get; init; }

    [JsonPropertyName("totalBytes")] public required int TotalBytes { get; init; }

    [JsonPropertyName("offset")] public required int Offset { get; init; }

    [JsonPropertyName("dataBase64")] public required string DataBase64 { get; init; }
}

public sealed class AgentImportPackage : ExtensibleJsonObject
{
    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("dotnet")] public required bool Dotnet { get; init; }

    [JsonPropertyName("state")] public required string State { get; init; }

    [JsonPropertyName("installedVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstalledVersion { get; init; }

    [JsonPropertyName("marketplaceName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MarketplaceName { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

public sealed class AgentImportUnresolved : ExtensibleJsonObject
{
    [JsonPropertyName("skills")] public required IReadOnlyList<string> Skills { get; init; }

    [JsonPropertyName("mcpServers")] public required IReadOnlyList<string> McpServers { get; init; }

    [JsonPropertyName("plugins")] public required IReadOnlyList<string> Plugins { get; init; }
}

public sealed class AgentImportPreview : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }

    [JsonPropertyName("kind")] public required string Kind { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("nameTaken")] public required bool NameTaken { get; init; }

    [JsonPropertyName("problems")] public required IReadOnlyList<string> Problems { get; init; }

    [JsonPropertyName("packages")] public required IReadOnlyList<AgentImportPackage> Packages { get; init; }

    [JsonPropertyName("unresolved")] public required AgentImportUnresolved Unresolved { get; init; }
}

public sealed class AgentImportUploadResult : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }

    [JsonPropertyName("receivedBytes")] public required int ReceivedBytes { get; init; }

    [JsonPropertyName("preview")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentImportPreview? Preview { get; init; }
}

public sealed class AgentImportCommitParams : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("source")] public required string Source { get; init; }

    [JsonPropertyName("packages")] public required IReadOnlyList<AgentPackageRef> Packages { get; init; }
}

public sealed class AgentImportDiscardParams : ExtensibleJsonObject
{
    [JsonPropertyName("importId")] public required string ImportId { get; init; }
}
