using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Channels;


/// <summary>
/// Declares that this client is an external channel adapter.
/// Sent inside <see cref="ClientConnectionCapabilities"/> during the initialize handshake.
/// See external-channel-adapter.md §5.1.
/// </summary>
public sealed class ChannelAdapterRuntimeCapability
{
    /// <summary>
    /// Canonical channel name (e.g. "telegram"). Must match the server-side
    /// ExternalChannels configuration key.
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// Structured delivery capability descriptor for <c>ext/channel/send</c>.
    /// Text and media delivery both use the unified send contract.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDeliveryCapabilitySnapshot? DeliveryCapabilities { get; set; }

    /// <summary>
    /// Optional channel-scoped tools declared by the adapter during initialize.
    /// These tools are only injected into matching-origin threads while the adapter remains connected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChannelToolSpec>? ChannelTools { get; set; }
}

public sealed class ChannelToolSpec
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON Schema describing the input arguments accepted by the tool.
    /// </summary>
    public JsonObject? InputSchema { get; set; }

    /// <summary>
    /// Optional JSON Schema describing the structured result returned by the tool.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; set; }

    /// <summary>
    /// Optional adapter-provided display metadata for richer tool UIs.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolDisplaySpec? Display { get; set; }

    /// <summary>
    /// Optional approval metadata describing which argument should be intercepted by the server
    /// before dispatching <c>ext/channel/toolCall</c>.
    /// This describes approval targets only; policy remains server-owned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalSpec? Approval { get; set; }

    public bool RequiresChatContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }
}

public sealed class ChannelToolApprovalSpec
{
    /// <summary>
    /// Server approval category, for example <c>file</c> or <c>shell</c>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// Name of the tool argument that contains the primary approval target.
    /// </summary>
    public string TargetArgument { get; set; } = string.Empty;

    /// <summary>
    /// Optional static operation label forwarded to the approval service.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; set; }

    /// <summary>
    /// Optional argument name whose runtime value is forwarded as the operation string.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperationArgument { get; set; }
}

public sealed class ChannelToolDisplaySpec
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
}

public sealed class ChannelToolInvocationRequest
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string CallId { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = [];

    public ChannelToolInvocationContext Context { get; set; } = new();
}

public sealed class ChannelToolInvocationContext
{
    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }
}

public sealed class ChannelToolInvocationResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChannelToolInvocationContentItem>? ContentItems { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? StructuredResult { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

public sealed class ChannelToolInvocationContentItem
{
    public string Type { get; set; } = "text";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }
}

public sealed class ChannelDeliveryCapabilitySnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StructuredDelivery { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaCapabilitySnapshot? Media { get; set; }
}

public sealed class ChannelMediaCapabilitySnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraintSnapshot? File { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraintSnapshot? Audio { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraintSnapshot? Image { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraintSnapshot? Video { get; set; }
}

public sealed class ChannelMediaConstraintSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? MaxBytes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedMimeTypes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AllowedExtensions { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsHostPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsUrl { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SupportsCaption { get; set; }
}

public sealed class ChannelDeliveryMediaSource
{
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HostPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataBase64 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ArtifactId { get; set; }
}

public sealed class ChannelDeliveryMessage
{
    public string Kind { get; set; } = "text";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Caption { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FileName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelDeliveryMediaSource? Source { get; set; }
}

public sealed class ChannelDeliveryRequest
{
    public string Target { get; set; } = string.Empty;

    public ChannelDeliveryMessage Message { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Metadata { get; set; }
}

public sealed class ChannelDeliveryResult
{
    public bool Delivered { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemoteMessageId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RemoteMediaId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}
