using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Channels;

public sealed class ChannelAdapterRuntimeCapability
{
    /// <summary>Must match the channel's <c>ExternalChannels</c> configuration key.</summary>
    public string ChannelName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public ChannelDeliveryCapabilitySnapshot DeliveryCapabilities { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChannelToolSpec>? ChannelTools { get; set; }
}

public sealed class ChannelToolSpec
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public JsonObject? InputSchema { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? OutputSchema { get; set; }

    /// <summary>Describes approval targets only; the server owns approval policy.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalSpec? Approval { get; set; }

    public bool RequiresChatContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }
}

public sealed class ChannelToolApprovalSpec
{
    public string Kind { get; set; } = string.Empty;

    public string TargetArgument { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Operation { get; set; }

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

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelHistoryCapabilitySnapshot? History { get; set; }
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
