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

    public string DisplayName { get; set; } = string.Empty;

    public ChannelDeliveryCapabilitySnapshot DeliveryCapabilities { get; set; } = new();

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
    public ChannelTextCapabilitySnapshot Text { get; set; } = new();

    public ChannelTargetCapabilitySnapshot Targets { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaCapabilitySnapshot? Media { get; set; }
}

public sealed class ChannelTextCapabilitySnapshot
{
    public string Format { get; set; } = ChannelTextFormats.Plain;
}

public static class ChannelTextFormats
{
    public const string Plain = "plain";
    public const string Markdown = "markdown";
}

public sealed class ChannelTargetCapabilitySnapshot
{
    public bool Group { get; set; }

    public string Direct { get; set; } = ChannelDirectReach.None;
}

public static class ChannelDirectReach
{
    public const string Any = "any";
    public const string KnownOnly = "knownOnly";
    public const string None = "none";
}

public sealed record ChannelChatDescriptor(string Kind, string? Label, string Membership);

public static class ChannelChatKinds
{
    public const string Direct = "direct";
    public const string Group = "group";
    public const string Topic = "topic";
}

public static class ChannelChatMemberships
{
    public const string Shared = "shared";
    public const string Sender = "sender";
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

    public string DeliveryId { get; set; } = string.Empty;

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
