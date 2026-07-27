using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

public sealed class ThreadAppBindingEnableParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
}

public sealed class ThreadAppBindingEnableResult
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public DateTimeOffset ExpiresAt { get; set; }
    [JsonIgnore]
    public string RequestToken { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppHandoffWire? Handoff { get; set; }
}

public sealed class AppBindingRequestGetParams
{
    public string BindingRequestId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestToken { get; set; }
}

public sealed class AppBindingRequestWire
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AppBindingActivateParams
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? BearerExpiresAt { get; set; }
}

public sealed class AppBindingRebindParams
{
    public string BindingId { get; set; } = string.Empty;
    public long AuthorityRevision { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? BearerExpiresAt { get; set; }
}

public sealed class ThreadAppBindingConfirmCapabilitiesParams
{
    public string ThreadId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public long CandidateRevision { get; set; }
    public string Decision { get; set; } = string.Empty;
}

public sealed class AppBindingWire
{
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public long AuthorityRevision { get; set; }
    public long ApprovedCapabilityRevision { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CandidateCapabilityRevision { get; set; }
    public List<AppBindingToolCapabilityWire> ApprovedTools { get; set; } = [];
    public List<AppBindingCapabilityChangeWire> PendingChanges { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelTargetWire? SocialTarget { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppBindingToolCapabilityWire
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonObject InputSchema { get; set; } = new();
    public List<string> Visibility { get; set; } = [];
    public JsonObject Annotations { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppBindingUiCapabilityWire? Ui { get; set; }
}

public sealed class AppBindingUiCapabilityWire
{
    public string ResourceUri { get; set; } = string.Empty;
    public List<string> ConnectDomains { get; set; } = [];
    public List<string> ResourceDomains { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public string SecurityHash { get; set; } = string.Empty;
}

public sealed class AppBindingCapabilityChangeWire
{
    public string Kind { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
