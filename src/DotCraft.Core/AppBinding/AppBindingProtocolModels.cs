using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DotCraft.AppBinding;

internal sealed class ThreadAppBindingEnableOutcome
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public DateTimeOffset ExpiresAt { get; set; }
    [JsonIgnore]
    public string RequestToken { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppHandoffDescriptor? Handoff { get; set; }
}

internal sealed class AppBindingRequestQuery
{
    public string BindingRequestId { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestToken { get; set; }
}

internal sealed class AppBindingRequestSnapshot
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public DateTimeOffset ExpiresAt { get; set; }
}

internal sealed class AppBindingActivateCommand
{
    public string BindingRequestId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? BearerExpiresAt { get; set; }
}

internal sealed class AppBindingRebindCommand
{
    public string BindingId { get; set; } = string.Empty;
    public long AuthorityRevision { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Bearer { get; set; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? BearerExpiresAt { get; set; }
}

internal sealed class ThreadAppBindingConfirmCapabilitiesCommand
{
    public string ThreadId { get; set; } = string.Empty;
    public string BindingId { get; set; } = string.Empty;
    public long CandidateRevision { get; set; }
    public string Decision { get; set; } = string.Empty;
}

internal sealed class AppBindingSnapshot
{
    public string BindingId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string State { get; set; } = AppBindingStates.Connecting;
    public long AuthorityRevision { get; set; }
    public long ApprovedCapabilityRevision { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CandidateCapabilityRevision { get; set; }
    public List<AppBindingToolCapability> ApprovedTools { get; set; } = [];
    public List<AppBindingCapabilityChange> PendingChanges { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelTarget? SocialTarget { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AppBindingToolCapability
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public JsonObject InputSchema { get; set; } = new();
    public List<string> Visibility { get; set; } = [];
    public JsonObject Annotations { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppBindingUiCapability? Ui { get; set; }
}

public sealed class AppBindingUiCapability
{
    public string ResourceUri { get; set; } = string.Empty;
    public List<string> ConnectDomains { get; set; } = [];
    public List<string> ResourceDomains { get; set; } = [];
    public List<string> Permissions { get; set; } = [];
    public string SecurityHash { get; set; } = string.Empty;
}

public sealed class AppBindingCapabilityChange
{
    public string Kind { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}
