using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using DotCraft.Protocol;
using DotCraft.Sessions;

namespace DotCraft.Teams;

public static class TeamsConstants
{
    public const string ToolNamespace = "teams";
    public const string UserId = "dotcraft-teams";
    public const string ChannelName = "teams";
}

public static class TeamMissionStatuses
{
    public const string Planning = "planning";
    public const string Active = "active";
    public const string AwaitingLeaderReview = "awaitingLeaderReview";
    public const string Done = "done";
    public const string Cancelled = "cancelled";
}

public static class TeamTaskStatuses
{
    public const string Pending = "pending";
    public const string WaitingDependencies = "waitingDependencies";
    public const string Ready = "ready";
    public const string Running = "running";
    public const string Blocked = "blocked";
    public const string Review = "review";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class TeamMessageStatuses
{
    public const string Recorded = "recorded";
    public const string DeliveredToTurn = "deliveredToTurn";
}

public static class TeamMessageKinds
{
    public const string Info = "info";
    public const string Request = "request";
    public const string Handoff = "handoff";
    public const string Revision = "revision";
    public const string Decision = "decision";
    public const string Blocker = "blocker";
    public const string Synthesis = "synthesis";
}

public sealed class TeamsStateDocument
{
    public int SchemaVersion { get; set; } = TeamsStateStore.CurrentSchemaVersion;

    public TeamRecord Team { get; set; } = new();

    public List<TeamMemberRecord> Members { get; set; } = [];

    public List<MissionRecord> Missions { get; set; } = [];

    public List<MissionThreadRecord> MissionThreads { get; set; } = [];

    public List<TeamTaskRecord> Tasks { get; set; } = [];

    public List<TeamMessageRecord> Messages { get; set; } = [];

    public List<MailboxDigestRecord> MailboxDigests { get; set; } = [];

    public List<ArtifactRefRecord> Artifacts { get; set; } = [];
}

public sealed class TeamRecord
{
    public string TeamId { get; set; } = "default";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

public class TeamMemberRecord
{
    public string MemberId { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentProfileId { get; set; }

    public string AvatarAccent { get; set; } = string.Empty;

    public double DeskX { get; set; }

    public double DeskY { get; set; }
}

public sealed class MissionRecord
{
    public string MissionId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Plan { get; set; } = string.Empty;

    public string Status { get; set; } = TeamMissionStatuses.Planning;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionSummary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinalResponse { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScratchpadPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LeaderContinuationQueuedInputId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ArchivedAt { get; set; }

    public string LeaderThreadId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionQueuedInputId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CompletionNotifiedAt { get; set; }
}

public class MissionThreadRecord
{
    public string MissionId { get; set; } = string.Empty;

    public string MemberId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public string Status { get; set; } = "idle";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentTaskId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueuedInputId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class TeamTaskRecord
{
    public string TaskId { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public string MissionId { get; set; } = string.Empty;

    public string AssigneeMemberId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public string Status { get; set; } = TeamTaskStatuses.Pending;

    public string Kind { get; set; } = "work";

    public bool RequiredForMission { get; set; } = true;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequiresLeaderSynthesis { get; set; }

    public List<string> DependsOnTaskIds { get; set; } = [];

    public List<string> BlockedOnTaskIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlockedReason { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LatestUpdate { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OutputSummary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Metadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueuedInputId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SynthesisMessageId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CompletionRecoveryPending { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CompletionRecoveryQueuedInputId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LeaderNotifiedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CompletionRecoveryAttempts { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string Digest { get; set; } = string.Empty;
}

public sealed class MailboxDigestRecord
{
    public string DigestId { get; set; } = string.Empty;

    public string MemberId { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TeamMessageRecord
{
    public string MessageId { get; set; } = string.Empty;

    public string MissionId { get; set; } = string.Empty;

    public string FromMemberId { get; set; } = string.Empty;

    public string ToMemberId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaskId { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Kind { get; set; } = "info";

    public bool RequiresAction { get; set; }

    public string Status { get; set; } = TeamMessageStatuses.Recorded;

    public List<string> ArtifactIds { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Metadata { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeliveredQueuedInputId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? DeliveredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ArtifactRefRecord
{
    public string ArtifactId { get; set; } = string.Empty;

    public string Alias { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceTaskId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceMessageId { get; set; }

    public string MemberId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Uri { get; set; } = string.Empty;

    public string Kind { get; set; } = "reference";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Summary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Metadata { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class TeamsTeamViewSnapshot
{
    public TeamRecord Team { get; set; } = new();

    public TeamsTeamStatistics Stats { get; set; } = new();

    public List<TeamMemberSnapshot> Members { get; set; } = [];

    public List<MissionRecord> Missions { get; set; } = [];

    public List<MissionRecord> ArchivedMissions { get; set; } = [];

    public List<MissionThreadSnapshot> MissionThreads { get; set; } = [];

    public List<TeamTaskRecord> Tasks { get; set; } = [];

    public List<TeamMessageRecord> Messages { get; set; } = [];

    public List<MailboxDigestRecord> MailboxDigests { get; set; } = [];

    public List<ArtifactRefRecord> Artifacts { get; set; } = [];
}

public sealed class TeamsTeamStatistics
{
    public int RunningMembers { get; set; }

    public int QueuedInputs { get; set; }

    public int TotalTasks { get; set; }

    public int CompletedTasks { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    public long CachedInputTokens { get; set; }

    public long TotalTokens { get; set; }
}

public sealed class TeamMemberSnapshot : TeamMemberRecord
{
    public string Status { get; set; } = "idle";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentTaskId { get; set; }

    public int QueuedInputCount { get; set; }

    public bool Running { get; set; }

    public bool WaitingOnApproval { get; set; }

    public bool WaitingOnInput { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TeamMemberAgentProfileSnapshot? AgentProfile { get; set; }
}

public sealed class TeamMemberAgentProfileSnapshot
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestedId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fingerprint { get; set; }

    public bool Missing { get; set; }

    public bool FallbackUsed { get; set; }

    public bool Valid { get; set; } = true;

    public List<TeamMemberAgentProfileIssue> Diagnostics { get; set; } = [];
}

public sealed class TeamMemberAgentProfileIssue
{
    public string Severity { get; set; } = "error";

    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
}

public sealed class MissionThreadSnapshot : MissionThreadRecord
{
    public int QueuedInputCount { get; set; }

    public bool Running { get; set; }

    public bool WaitingOnApproval { get; set; }

    public bool WaitingOnInput { get; set; }
}

public sealed class TeamsMissionCreateCommand
{
    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;
}

public sealed class TeamsMissionCreateOutcome
{
    public MissionRecord Mission { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public QueuedTurnInput? QueuedInput { get; set; }

    public TeamsTeamViewSnapshot Team { get; set; } = new();
}

public sealed class TeamsMissionCancelCommand
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class TeamsMissionArchiveCommand
{
    public string MissionId { get; set; } = string.Empty;
}

public sealed class TeamsMemberOpenThreadQuery
{
    public string MemberId { get; set; } = string.Empty;

    public string MissionId { get; set; } = string.Empty;

    public string TaskId { get; set; } = string.Empty;
}

public sealed class TeamsMemberOpenThreadOutcome
{
    public string ThreadId { get; set; } = string.Empty;
}
