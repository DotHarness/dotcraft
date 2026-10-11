using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class FsReadFileParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }
}

public sealed class FsReadFileResult : ExtensibleJsonObject
{
    [JsonPropertyName("dataBase64")] public required string DataBase64 { get; init; }
}

public sealed class FsWriteFileParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("dataBase64")] public required string DataBase64 { get; init; }
}

public sealed class FsCreateDirectoryParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("recursive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Recursive { get; init; }
}

public sealed class FsGetMetadataParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }
}

public sealed class FsGetMetadataResult : ExtensibleJsonObject
{
    [JsonPropertyName("isDirectory")] public required bool IsDirectory { get; init; }

    [JsonPropertyName("isFile")] public required bool IsFile { get; init; }

    [JsonPropertyName("isSymlink")] public required bool IsSymlink { get; init; }

    [JsonPropertyName("createdAtMs")]
    [JsonSafeInteger]
    public required long CreatedAtMs { get; init; }

    [JsonPropertyName("modifiedAtMs")]
    [JsonSafeInteger]
    public required long ModifiedAtMs { get; init; }
}

public sealed class FsReadDirectoryParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }
}

public sealed class FsReadDirectoryEntry : ExtensibleJsonObject
{
    [JsonPropertyName("fileName")] public required string FileName { get; init; }

    [JsonPropertyName("isDirectory")] public required bool IsDirectory { get; init; }

    [JsonPropertyName("isFile")] public required bool IsFile { get; init; }
}

public sealed class FsReadDirectoryResult : ExtensibleJsonObject
{
    [JsonPropertyName("entries")] public required IReadOnlyList<FsReadDirectoryEntry> Entries { get; init; }
}

public sealed class FsRemoveParams : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }

    [JsonPropertyName("recursive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Recursive { get; init; }

    [JsonPropertyName("force")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Force { get; init; }
}

public sealed class FsCopyParams : ExtensibleJsonObject
{
    [JsonPropertyName("sourcePath")] public required string SourcePath { get; init; }

    [JsonPropertyName("destinationPath")] public required string DestinationPath { get; init; }

    [JsonPropertyName("recursive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Optional<bool> Recursive { get; init; }
}

public sealed class FsWatchParams : ExtensibleJsonObject
{
    [JsonPropertyName("watchId")] public required string WatchId { get; init; }

    [JsonPropertyName("path")] public required string Path { get; init; }
}

public sealed class FsWatchResult : ExtensibleJsonObject
{
    [JsonPropertyName("path")] public required string Path { get; init; }
}

public sealed class FsUnwatchParams : ExtensibleJsonObject
{
    [JsonPropertyName("watchId")] public required string WatchId { get; init; }
}

public sealed class FsChangedNotification : ExtensibleJsonObject
{
    [JsonPropertyName("watchId")] public required string WatchId { get; init; }

    [JsonPropertyName("changedPaths")] public required IReadOnlyList<string> ChangedPaths { get; init; }
}
