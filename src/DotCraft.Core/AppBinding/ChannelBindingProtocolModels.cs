using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

/// <summary>Result for <c>thread/channelBindings/request/create</c>.</summary>
public sealed class ThreadChannelBindingRequestCreateOutcome
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

public sealed class ChannelBindingAcceptCommand
{
    public string Code { get; set; } = string.Empty;
    public ChannelTarget Target { get; set; } = new();
}

public sealed class ChannelBindingRebindCommand
{
    public string BindingId { get; set; } = string.Empty;
    public long AuthorityRevision { get; set; }
    public ChannelTarget Target { get; set; } = new();
}
