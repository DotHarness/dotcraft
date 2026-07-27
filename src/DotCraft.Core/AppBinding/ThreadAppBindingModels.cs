using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.AppBinding;

public sealed class ThreadAppContextBlocksListParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeInactive { get; set; }
}

public sealed class ThreadAppContextBlocksListResult
{
    public List<ThreadAppContextBlockWire> Blocks { get; set; } = [];
}

public sealed class ThreadAppBindingsListParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeRevoked { get; set; }
}

public sealed class ThreadAppBindingsListResult
{
    public List<ThreadAppBindingWire> Bindings { get; set; } = [];
}

public sealed class ThreadAppBindingRevokeParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class ThreadAppBindingRevokeResult
{
    public string BindingId { get; set; } = string.Empty;

    public string State { get; set; } = AppBindingStates.Revoked;
}

public sealed class ThreadAppBindingRefreshParams
{
    public string ThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingId { get; set; }
}

public sealed class ThreadAppBindingRefreshResult
{
    public List<ThreadAppBindingRefreshWire> Bindings { get; set; } = [];
}

public sealed class ThreadAppBindingWire
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindingRequestId { get; set; }

    public string BindingId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    [JsonIgnore]
    public string? GrantId { get; set; }

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
    public int AttachedToolCount { get; set; }

    [JsonIgnore]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore]
    public DateTimeOffset LastChangedAt { get; set; }

    [JsonIgnore]
    public string? ApprovalMode { get; set; }

    [JsonIgnore]
    public string? AuditRef { get; set; }

    [JsonIgnore]
    public string? Diagnostic { get; set; }

    [JsonIgnore]
    public string? BindingKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SocialChannelTargetWire? SocialTarget { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ExposureRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long AuthorityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ApprovedCapabilityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CandidateCapabilityRevision { get; set; }

    public List<AppBindingToolCapabilityWire> ApprovedTools { get; set; } = [];

    public List<AppBindingCapabilityChangeWire> PendingChanges { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }
}

public sealed class ThreadAppContextBlockWire
{
    public string BlockId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string BindingId { get; set; } = string.Empty;

    public string AppId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Version { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; set; }

    public bool Active { get; set; }
}

public sealed class ThreadAppBindingSummaryWire
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
    public SocialChannelTargetWire? SocialTarget { get; set; }

    [JsonIgnore]
    public long ExposureRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long AuthorityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ApprovedCapabilityRevision { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? CandidateCapabilityRevision { get; set; }

    public List<AppBindingToolCapabilityWire> ApprovedTools { get; set; } = [];

    public List<AppBindingCapabilityChangeWire> PendingChanges { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; set; }
}

public sealed class ThreadAppBindingRefreshWire
{
    public string BindingId { get; set; } = string.Empty;

    public string State { get; set; } = LegacyAppBindingStates.Pending;

    public int AttachedToolCount { get; set; }
}

public sealed class AppHandoffWire
{
    public string Mode { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Uri { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BindCode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Instructions { get; set; }
}

public sealed class AppBindingConfirmationWire
{
    public bool Required { get; set; } = true;

    public string Risk { get; set; } = AppBindingRisks.Read;

    public string Message { get; set; } = string.Empty;
}
