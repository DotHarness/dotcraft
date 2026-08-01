using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DotCraft.Protocol.AppServer;


/// <summary>
/// Declares that this client is an external channel adapter.
/// Sent inside <see cref="AppServerClientCapabilities"/> during the initialize handshake.
/// See external-channel-adapter.md §5.1.
/// </summary>
public sealed class ChannelAdapterCapability
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
    public ChannelDeliveryCapabilities? DeliveryCapabilities { get; set; }

    /// <summary>
    /// Optional channel-scoped tools declared by the adapter during initialize.
    /// These tools are only injected into matching-origin threads while the adapter remains connected.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ChannelToolDescriptor>? ChannelTools { get; set; }
}

public sealed class ChannelToolDescriptor
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
    public ChannelToolDisplay? Display { get; set; }

    /// <summary>
    /// Optional approval metadata describing which argument should be intercepted by the server
    /// before dispatching <c>ext/channel/toolCall</c>.
    /// This describes approval targets only; policy remains server-owned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelToolApprovalDescriptor? Approval { get; set; }

    public bool RequiresChatContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }
}

public sealed class ChannelToolApprovalDescriptor
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

public sealed class ChannelToolDisplay
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subtitle { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }
}

public sealed class ExtChannelToolCallParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string CallId { get; set; } = string.Empty;

    public string Tool { get; set; } = string.Empty;

    public JsonObject Arguments { get; set; } = [];

    public ExtChannelToolCallContext Context { get; set; } = new();
}

public sealed class ExtChannelToolCallContext
{
    public string ChannelName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelContext { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SenderId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }
}

public sealed class ExtChannelToolCallResult
{
    public bool Success { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExtChannelToolContentItem>? ContentItems { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonNode? StructuredResult { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }
}

public sealed class ExtChannelToolContentItem
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

public sealed class ChannelDeliveryCapabilities
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? StructuredDelivery { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaCapabilitySet? Media { get; set; }
}

public sealed class ChannelMediaCapabilitySet
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? File { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Audio { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Image { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelMediaConstraints? Video { get; set; }
}

public sealed class ChannelMediaConstraints
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

public sealed class ChannelMediaSource
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

public sealed class ChannelOutboundMessage
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
    public ChannelMediaSource? Source { get; set; }
}

public sealed class ExtChannelSendParams
{
    public string Target { get; set; } = string.Empty;

    public ChannelOutboundMessage Message { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Metadata { get; set; }
}

public sealed class ExtChannelSendResult
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

// ───── channel/status (Desktop runtime status, spec Section 20) ─────

/// <summary>
/// Runtime status for one social or external channel.
/// See <see cref="DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ChannelStatus"/> and spec Section 20.
/// </summary>
public sealed class ChannelStatusInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// UI grouping: <c>social</c> for native C# channels; <c>external</c> for adapter channels.
    /// </summary>
    public string Category { get; set; } = "social";

    /// <summary>
    /// <c>true</c> when the channel section in merged workspace config has Enabled = true.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// <c>true</c> when the channel service was actually started and is currently registered / connected.
    /// </summary>
    public bool Running { get; set; }
}

/// <summary>
/// Result for <see cref="DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ChannelStatus"/>.
/// </summary>
public sealed class ChannelStatusResult
{
    public List<ChannelStatusInfo> Channels { get; set; } = [];
}

// ───── channel/list (Desktop cross-channel picker) ─────

/// <summary>
/// One discoverable session channel for <see cref="DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ChannelList"/> (originChannel values).
/// </summary>
public sealed class ChannelInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// UI grouping: <c>builtin</c>, <c>social</c>, <c>system</c>, or <c>external</c>.
    /// </summary>
    public string Category { get; set; } = "builtin";
}

/// <summary>
/// Result for <see cref="DotCraft.Protocol.Contracts.AppServer.AppServerMethodNames.ChannelList"/>.
/// </summary>
public sealed class ChannelListResult
{
    public List<ChannelInfo> Channels { get; set; } = [];
}

// ───── externalChannel/* (external channel management) ─────

public sealed class ExternalChannelConfigWire
{
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "subprocess";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuiltinModule { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }
}

public sealed class ExternalChannelListResult
{
    public List<ExternalChannelConfigWire> Channels { get; set; } = [];
}

public sealed class ExternalChannelGetParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ExternalChannelGetResult
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelUpsertParams
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelUpsertResult
{
    public ExternalChannelConfigWire Channel { get; set; } = new();
}

public sealed class ExternalChannelRemoveParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class ExternalChannelRemoveResult
{
    public bool Removed { get; set; }
}

public sealed class ExternalChannelLogsParams
{
    public string Name { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Tail { get; set; }
}

public sealed class ExternalChannelLogsResult
{
    public string Name { get; set; } = string.Empty;
    public List<string> Lines { get; set; } = [];
}
