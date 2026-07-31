using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

public sealed class ThreadSocialBindingRequestCreateParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
}

/// <summary>Result for <c>thread/socialBindings/request/create</c>.</summary>
public sealed class ThreadSocialBindingRequestCreateResult
{
    [JsonPropertyName("bindingRequestId")]
    public string BindingRequestId { get; set; } = string.Empty;

    [JsonPropertyName("bindingId")]
    public string BindingId { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("channelName")]
    public string ChannelName { get; set; } = string.Empty;

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Payload for <c>app/connection/changed</c>.</summary>
public sealed class AppConnectionChangedNotification
{
    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
}

/// <summary>Payload for <c>app/binding/requested</c>.</summary>
public sealed class AppBindingRequestedNotification
{
    [JsonPropertyName("bindingRequestId")]
    public string BindingRequestId { get; set; } = string.Empty;

    [JsonPropertyName("bindingId")]
    public string BindingId { get; set; } = string.Empty;

    [JsonPropertyName("threadId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    [JsonPropertyName("channelName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; set; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Payload for <c>thread/appBindings/changed</c>.</summary>
public sealed class ThreadAppBindingsChangedNotification
{
    [JsonPropertyName("threadId")]
    public string ThreadId { get; set; } = string.Empty;

    [JsonPropertyName("bindingId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingId { get; set; }

    [JsonPropertyName("appId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }

    [JsonPropertyName("authorityRevision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AuthorityRevision { get; set; }

    [JsonPropertyName("previousState")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousState { get; set; }

    [JsonPropertyName("changeKind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChangeKind { get; set; }
}

public sealed class SocialBindingRequestGetParams
{
    public string Code { get; set; } = string.Empty;
}

public sealed class SocialBindingAcceptParams
{
    public string Code { get; set; } = string.Empty;
    public SocialChannelTargetWire Target { get; set; } = new();
}

public sealed class SocialBindingRebindParams
{
    public string BindingId { get; set; } = string.Empty;
    public long AuthorityRevision { get; set; }
    public SocialChannelTargetWire Target { get; set; } = new();
}
