using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

public sealed class ThreadAppBindingSummarySnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingRequestId { get; set; }

    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Icon { get; set; }

    [JsonIgnore]
    public string? ToolNamespace { get; set; }

    public string State { get; set; } = LegacyAppBindingStates.Pending;

    [JsonIgnore]
    public string ConnectionState { get; set; } = AppConnectionStates.NotConnected;

    public bool Managed { get; set; }

    public bool RequiresExternalConnection { get; set; } = true;

    [JsonIgnore]
    public List<string> GrantedScopes { get; set; } = [];

    [JsonIgnore]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public string? BindingKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelTarget? SocialTarget { get; set; }

    [JsonIgnore]
    public long ExposureRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long AuthorityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ApprovedCapabilityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CandidateCapabilityRevision { get; set; }

    public List<AppBindingToolCapability> ApprovedTools { get; set; } = [];

    public List<AppBindingCapabilityChange> PendingChanges { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }
}
internal sealed class AppHandoffDescriptor
{
    public string Mode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }
}
