using System.Text.Json;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.AppServer;

namespace DotCraft.Teams;

/// <summary>Explicit boundary between persisted Teams state/runtime projections and AppServer contracts.</summary>
internal static class TeamsContractMapper
{
    public static Contract.TeamsTeamViewResult ToContract(TeamsTeamViewSnapshot value) => new()
    {
        Team = DotCraft.Protocol.Optional<Contract.TeamRecord?>.FromValue(ToContract(value.Team)),
        Stats = ToContract(value.Stats),
        Members = value.Members.Select(ToContract).ToArray(),
        Missions = value.Missions.Select(ToContract).ToArray(),
        ArchivedMissions = value.ArchivedMissions.Select(ToContract).ToArray(),
        MissionThreads = value.MissionThreads.Select(ToContract).ToArray(),
        Tasks = value.Tasks.Select(ToContract).ToArray(),
        Messages = value.Messages.Select(ToContract).ToArray(),
        MailboxDigests = value.MailboxDigests.Select(ToContract).ToArray(),
        Artifacts = value.Artifacts.Select(ToContract).ToArray()
    };

    public static Contract.TeamsMissionCreateResult ToContract(TeamsMissionCreateOutcome value) => new()
    {
        Mission = ToContract(value.Mission),
        QueuedInput = value.QueuedInput is null
            ? default
            : DotCraft.Protocol.Optional<Contract.QueuedTurnInput?>.FromValue(
                TurnContractMapper.ToContract(value.QueuedInput)),
        Team = ToContract(value.Team)
    };

    public static Contract.TeamsMemberOpenThreadResult ToContract(TeamsMemberOpenThreadOutcome value) => new()
    {
        ThreadId = value.ThreadId
    };

    public static TeamsMissionCreateCommand FromContract(Contract.TeamsMissionCreateParams value) => new()
    {
        Title = Read(value.Title) ?? string.Empty,
        Prompt = Read(value.Prompt) ?? string.Empty
    };

    public static TeamsMissionCancelCommand FromContract(Contract.TeamsMissionCancelParams value) => new()
    {
        MissionId = Read(value.MissionId) ?? string.Empty
    };

    public static TeamsMissionArchiveCommand FromContract(Contract.TeamsMissionArchiveParams value) => new()
    {
        MissionId = Read(value.MissionId) ?? string.Empty
    };

    public static TeamsMemberOpenThreadQuery FromContract(Contract.TeamsMemberOpenThreadParams value) => new()
    {
        MemberId = Read(value.MemberId) ?? string.Empty,
        MissionId = Read(value.MissionId) ?? string.Empty,
        TaskId = Read(value.TaskId) ?? string.Empty
    };

    private static Contract.TeamRecord ToContract(TeamRecord value) => new()
    {
        TeamId = value.TeamId,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    private static Contract.TeamsTeamStats ToContract(TeamsTeamStatistics value) => new()
    {
        RunningMembers = value.RunningMembers,
        QueuedInputs = value.QueuedInputs,
        TotalTasks = value.TotalTasks,
        CompletedTasks = value.CompletedTasks,
        InputTokens = value.InputTokens,
        OutputTokens = value.OutputTokens,
        CachedInputTokens = value.CachedInputTokens,
        TotalTokens = value.TotalTokens
    };

    private static Contract.TeamMemberView ToContract(TeamMemberSnapshot value) => new()
    {
        MemberId = value.MemberId,
        Role = value.Role,
        DisplayName = value.DisplayName,
        Description = value.Description,
        AgentProfileId = OmitIfNull(value.AgentProfileId),
        AvatarAccent = value.AvatarAccent,
        DeskX = value.DeskX,
        DeskY = value.DeskY,
        Status = value.Status,
        CurrentTaskId = OmitIfNull(value.CurrentTaskId),
        QueuedInputCount = value.QueuedInputCount,
        Running = value.Running,
        WaitingOnApproval = value.WaitingOnApproval,
        WaitingOnInput = value.WaitingOnInput,
        AgentProfile = value.AgentProfile is null
            ? default
            : DotCraft.Protocol.Optional<Contract.TeamMemberAgentProfileView?>.FromValue(
                ToContract(value.AgentProfile))
    };

    private static Contract.TeamMemberAgentProfileView ToContract(TeamMemberAgentProfileSnapshot value) => new()
    {
        RequestedId = OmitIfNull(value.RequestedId),
        ActiveId = OmitIfNull(value.ActiveId),
        Source = OmitIfNull(value.Source),
        Fingerprint = OmitIfNull(value.Fingerprint),
        Missing = value.Missing,
        FallbackUsed = value.FallbackUsed,
        Valid = value.Valid,
        Diagnostics = value.Diagnostics.Select(ToContract).ToArray()
    };

    private static Contract.TeamMemberAgentProfileDiagnostic ToContract(TeamMemberAgentProfileIssue value) => new()
    {
        Severity = value.Severity,
        Code = value.Code,
        Message = value.Message
    };

    private static Contract.MissionRecord ToContract(MissionRecord value) => new()
    {
        MissionId = value.MissionId,
        Title = value.Title,
        Prompt = value.Prompt,
        Plan = value.Plan,
        Status = value.Status,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
        CompletedAt = OmitIfNull(value.CompletedAt),
        CompletionSummary = OmitIfNull(value.CompletionSummary),
        FinalResponse = OmitIfNull(value.FinalResponse),
        ScratchpadPath = OmitIfNull(value.ScratchpadPath),
        LeaderContinuationQueuedInputId = OmitIfNull(value.LeaderContinuationQueuedInputId),
        ArchivedAt = OmitIfNull(value.ArchivedAt),
        LeaderThreadId = value.LeaderThreadId,
        OriginThreadId = OmitIfNull(value.OriginThreadId),
        CompletionQueuedInputId = OmitIfNull(value.CompletionQueuedInputId),
        CompletionNotifiedAt = OmitIfNull(value.CompletionNotifiedAt)
    };

    private static Contract.MissionThreadView ToContract(MissionThreadSnapshot value) => new()
    {
        MissionId = value.MissionId,
        MemberId = value.MemberId,
        ThreadId = value.ThreadId,
        Status = value.Status,
        CurrentTaskId = OmitIfNull(value.CurrentTaskId),
        QueuedInputId = OmitIfNull(value.QueuedInputId),
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
        ArchivedAt = OmitIfNull(value.ArchivedAt),
        QueuedInputCount = value.QueuedInputCount,
        Running = value.Running,
        WaitingOnApproval = value.WaitingOnApproval,
        WaitingOnInput = value.WaitingOnInput
    };

    private static Contract.TeamTaskRecord ToContract(TeamTaskRecord value) => new()
    {
        TaskId = value.TaskId,
        Alias = value.Alias,
        MissionId = value.MissionId,
        AssigneeMemberId = value.AssigneeMemberId,
        Title = value.Title,
        Prompt = value.Prompt,
        Status = value.Status,
        Kind = value.Kind,
        RequiredForMission = value.RequiredForMission,
        RequiresLeaderSynthesis = value.RequiresLeaderSynthesis,
        DependsOnTaskIds = value.DependsOnTaskIds.ToArray(),
        BlockedOnTaskIds = value.BlockedOnTaskIds.ToArray(),
        BlockedReason = OmitIfNull(value.BlockedReason),
        LatestUpdate = OmitIfNull(value.LatestUpdate),
        OutputSummary = OmitIfNull(value.OutputSummary),
        Metadata = ToOptionalJson(value.Metadata),
        QueuedInputId = OmitIfNull(value.QueuedInputId),
        SynthesisMessageId = OmitIfNull(value.SynthesisMessageId),
        CompletionRecoveryPending = value.CompletionRecoveryPending,
        CompletionRecoveryQueuedInputId = OmitIfNull(value.CompletionRecoveryQueuedInputId),
        LeaderNotifiedAt = OmitIfNull(value.LeaderNotifiedAt),
        CompletionRecoveryAttempts = value.CompletionRecoveryAttempts,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt,
        Digest = value.Digest
    };

    private static Contract.TeamMessageRecord ToContract(TeamMessageRecord value) => new()
    {
        MessageId = value.MessageId,
        MissionId = value.MissionId,
        FromMemberId = value.FromMemberId,
        ToMemberId = value.ToMemberId,
        TaskId = OmitIfNull(value.TaskId),
        Content = value.Content,
        Kind = value.Kind,
        RequiresAction = value.RequiresAction,
        Status = value.Status,
        ArtifactIds = value.ArtifactIds.ToArray(),
        Metadata = ToOptionalJson(value.Metadata),
        DeliveredQueuedInputId = OmitIfNull(value.DeliveredQueuedInputId),
        DeliveredAt = OmitIfNull(value.DeliveredAt),
        CreatedAt = value.CreatedAt
    };

    private static Contract.MailboxDigestRecord ToContract(MailboxDigestRecord value) => new()
    {
        DigestId = value.DigestId,
        MemberId = value.MemberId,
        Content = value.Content,
        UpdatedAt = value.UpdatedAt
    };

    private static Contract.ArtifactRefRecord ToContract(ArtifactRefRecord value) => new()
    {
        ArtifactId = value.ArtifactId,
        Alias = value.Alias,
        TaskId = value.TaskId,
        SourceTaskId = OmitIfNull(value.SourceTaskId),
        SourceMessageId = OmitIfNull(value.SourceMessageId),
        MemberId = value.MemberId,
        Title = value.Title,
        Uri = value.Uri,
        Kind = value.Kind,
        Format = OmitIfNull(value.Format),
        Summary = OmitIfNull(value.Summary),
        Metadata = ToOptionalJson(value.Metadata),
        CreatedAt = value.CreatedAt
    };

    private static DotCraft.Protocol.Optional<JsonElement?> ToOptionalJson(object? value) =>
        value is null
            ? default
            : DotCraft.Protocol.Optional<JsonElement?>.FromValue(
                JsonSerializer.SerializeToElement(value));

    private static T? Read<T>(DotCraft.Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);
}
