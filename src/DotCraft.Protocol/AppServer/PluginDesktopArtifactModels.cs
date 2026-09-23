using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

public sealed class PluginDesktopReadParams : ExtensibleJsonObject
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("revision")]
    public required string Revision { get; init; }

    [JsonPropertyName("offset")]
    [JsonSafeInteger]
    public required long Offset { get; init; }
}

public sealed class PluginDesktopReadResult : ExtensibleJsonObject
{
    [JsonPropertyName("totalBytes")]
    [JsonSafeInteger]
    public required long TotalBytes { get; init; }

    [JsonPropertyName("dataBase64")]
    public required string DataBase64 { get; init; }
}
