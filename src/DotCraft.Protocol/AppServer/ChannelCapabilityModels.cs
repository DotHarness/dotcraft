using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

/// <summary>A channel-scoped tool declared by an adapter during initialization.</summary>
public sealed class ChannelToolDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; init; }

    [JsonPropertyName("outputSchema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? OutputSchema { get; init; }

    [JsonPropertyName("approval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; init; }

    [JsonPropertyName("requiresChatContext")]
    public bool RequiresChatContext { get; init; }

    [JsonPropertyName("deferLoading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; init; }
}

/// <summary>Server-owned approval target metadata for a channel tool.</summary>
public sealed class ChannelToolApprovalDescriptor : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("targetArgument")]
    public required string TargetArgument { get; init; }

    [JsonPropertyName("operation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; init; }

    [JsonPropertyName("operationArgument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationArgument { get; init; }
}

public sealed class ChannelDeliveryCapabilities : ExtensibleJsonObject
{
    [JsonPropertyName("text")]
    public required ChannelTextCapability Text { get; init; }

    [JsonPropertyName("targets")]
    public required ChannelTargetCapability Targets { get; init; }

    [JsonPropertyName("media")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaCapabilitySet? Media { get; init; }
}

public sealed class ChannelTextCapability : ExtensibleJsonObject
{
    [JsonPropertyName("format")]
    public required string Format { get; init; }
}

public sealed class ChannelTargetCapability : ExtensibleJsonObject
{
    [JsonPropertyName("group")]
    public required bool Group { get; init; }

    [JsonPropertyName("direct")]
    public required string Direct { get; init; }
}

public sealed class ChannelChat : ExtensibleJsonObject
{
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; init; }

    [JsonPropertyName("membership")]
    public required string Membership { get; init; }
}

/// <summary>Per-media-kind delivery constraints.</summary>
public sealed class ChannelMediaCapabilitySet : ExtensibleJsonObject
{
    [JsonPropertyName("file")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? File { get; init; }

    [JsonPropertyName("audio")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Audio { get; init; }

    [JsonPropertyName("image")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Image { get; init; }

    [JsonPropertyName("video")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Video { get; init; }
}

/// <summary>Delivery limits and supported source forms for one media kind.</summary>
public sealed class ChannelMediaConstraints : ExtensibleJsonObject
{
    [JsonPropertyName("maxBytes")]
    [JsonSafeInteger]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxBytes { get; init; }

    [JsonPropertyName("allowedMimeTypes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedMimeTypes { get; init; }

    [JsonPropertyName("allowedExtensions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? AllowedExtensions { get; init; }

    [JsonPropertyName("supportsHostPath")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsHostPath { get; init; }

    [JsonPropertyName("supportsUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsUrl { get; init; }

    [JsonPropertyName("supportsBase64")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsBase64 { get; init; }

    [JsonPropertyName("supportsCaption")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsCaption { get; init; }
}
