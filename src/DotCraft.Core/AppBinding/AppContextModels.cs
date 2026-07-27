using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

public sealed class AppBindingContextUpsertParams
{
    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Version { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; set; }
}
public sealed class AppBindingContextUpsertResult
{
    public ThreadAppContextBlockWire Block { get; set; } = new();
}

public sealed class AppBindingContextRemoveParams
{
    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string GrantId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;
}

public sealed class AppBindingContextRemoveResult
{
    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string BlockId { get; set; } = string.Empty;

    public bool Removed { get; set; }
}

public sealed class AppThreadInputEnqueueParams
{
    public string BindingId { get; set; } = string.Empty;

    // Read-only projection fields. App Binding authorization is derived
    // exclusively from BindingId and these values never cross the wire.
    [JsonIgnore]
    public string AppId { get; set; } = string.Empty;

    [JsonIgnore]
    public string GrantId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayText { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerLabel { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TriggerRefId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StartPolicy { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }
}

public sealed class AppThreadInputEnqueueResult
{
    public QueuedTurnInput QueuedInput { get; set; } = new();

    public List<QueuedTurnInput> QueuedInputs { get; set; } = [];
}
