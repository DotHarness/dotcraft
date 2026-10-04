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
