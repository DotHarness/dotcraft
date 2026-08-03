using System.Text.Json;
using DotCraft.AppBinding;
using Domain = DotCraft.Sessions;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;
using ContextUsageSnapshot = DotCraft.Sessions.Wire.ContextUsageSnapshot;
using ThreadGoal = DotCraft.Sessions.ThreadGoal;
using ThreadSource = DotCraft.Sessions.ThreadSource;

namespace DotCraft.AppServer;

internal static class ThreadContractMapper
{
    public static Contract.ThreadSummary ToContract(Domain.ThreadSummary value) => new()
    {
        Id = value.Id,
        UserId = value.UserId,
        Status = WireString(value.Status),
        DisplayName = value.DisplayName,
        WorkspacePath = value.WorkspacePath,
        OriginChannel = value.OriginChannel,
        ChannelContext = value.ChannelContext,
        Source = ToContract(value.Source),
        ForkedFromId = value.ForkedFromId,
        Ephemeral = value.Ephemeral,
        Worktree = DotCraft.Protocol.Optional<Contract.ThreadWorktreeInfo?>.FromValue(
            value.Worktree is null ? null : WorktreeContractMapper.ToContract(value.Worktree)),
        CreatedAt = value.CreatedAt,
        LastActiveAt = value.LastActiveAt,
        TurnCount = value.TurnCount,
        Runtime = value.Runtime is null ? null : ToContract(value.Runtime),
        Goal = value.Goal is null ? null : ToContract(value.Goal),
        AppBindings = value.AppBindings?.Select(ToContract).ToArray(),
        OriginApp = value.OriginApp is null ? null : ToContract(value.OriginApp),
        OriginPresentation = value.OriginPresentation is null ? null : ToContract(value.OriginPresentation),
        Metadata = value.Metadata,
        Turns = null
    };

    public static Contract.ThreadSource ToContract(Domain.ThreadSource value) => new()
    {
        Kind = value.Kind,
        SpawnedFromThreadId = value.SpawnedFromThreadId,
        SubAgent = value.SubAgent is null ? null : ToContract(value.SubAgent)
    };

    private static Contract.SubAgentThreadSource ToContract(Domain.SubAgentThreadSource value) => new()
    {
        ParentThreadId = value.ParentThreadId,
        ParentTurnId = value.ParentTurnId,
        SpawnCallId = value.SpawnCallId,
        RootThreadId = value.RootThreadId,
        Depth = value.Depth,
        AgentPath = value.AgentPath,
        TaskName = value.TaskName,
        AgentNickname = value.AgentNickname,
        AgentRole = value.AgentRole,
        ProfileName = value.ProfileName,
        RuntimeType = value.RuntimeType,
        SupportsSendInput = value.SupportsSendInput,
        SupportsResume = value.SupportsResume,
        SupportsSendMessage = value.SupportsSendMessage,
        SupportsFollowupTask = value.SupportsFollowupTask,
        SupportsClose = value.SupportsClose
    };

    private static Contract.ThreadRuntimeState ToContract(Domain.ThreadSummaryRuntime value) => new()
    {
        Running = value.Running,
        WaitingOnApproval = value.WaitingOnApproval,
        WaitingOnInput = value.WaitingOnInput,
        WaitingOnPlanConfirmation = value.WaitingOnPlanConfirmation,
        Busy = value.Busy,
        MaintenanceKind = OmitIfNull(value.MaintenanceKind)
    };

    public static Contract.ThreadGoal ToContract(Domain.ThreadGoalSnapshot value) => new()
    {
        ThreadId = value.ThreadId,
        Objective = value.Objective,
        Status = value.Status,
        TokenBudget = DotCraft.Protocol.Optional<long?>.FromValue(value.TokenBudget),
        TokensUsed = value.TokensUsed,
        TimeUsedSeconds = value.TimeUsedSeconds,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    public static Contract.ContextUsageSnapshot ToContract(ContextUsageSnapshot value) => new()
    {
        Tokens = value.Tokens,
        ContextWindow = value.ContextWindow,
        AutoCompactThreshold = value.AutoCompactThreshold,
        WarningThreshold = value.WarningThreshold,
        ErrorThreshold = value.ErrorThreshold,
        PercentLeft = value.PercentLeft,
        Source = OmitIfNull(value.Source),
        IsEstimate = value.IsEstimate
    };

    public static Contract.ThreadOriginApp ToContract(Domain.ThreadOriginAppSnapshot value) => new()
    {
        AppId = value.AppId,
        DisplayName = value.DisplayName,
        Icon = value.Icon,
        MemberId = value.MemberId
    };

    public static Contract.ThreadOriginPresentation ToContract(
        Domain.ThreadOriginPresentationSnapshot value) => new()
    {
        SourceId = value.SourceId,
        DisplayName = value.DisplayName,
        Icon = value.Icon,
        SubjectId = value.SubjectId,
        SubjectKind = value.SubjectKind
    };

    public static Contract.ThreadAppBindingSummary ToContract(ThreadAppBindingSummarySnapshot value) => new()
    {
        BindingRequestId = OmitIfNull(value.BindingRequestId),
        ThreadId = value.ThreadId,
        BindingId = value.BindingId,
        AppId = value.AppId,
        DisplayName = OmitIfNull(value.DisplayName),
        Icon = OmitIfNull(value.Icon),
        State = value.State,
        Managed = value.Managed,
        RequiresExternalConnection = value.RequiresExternalConnection,
        SocialTarget = value.SocialTarget is null
            ? default
            : DotCraft.Protocol.Optional<Contract.SocialChannelTarget?>.FromValue(
                ToContract(value.SocialTarget)),
        AuthorityRevision = value.AuthorityRevision == 0 ? default : value.AuthorityRevision,
        ApprovedCapabilityRevision = value.ApprovedCapabilityRevision == 0
            ? default
            : value.ApprovedCapabilityRevision,
        CandidateCapabilityRevision = OmitIfNull(value.CandidateCapabilityRevision),
        ApprovedTools = DotCraft.Protocol.Optional<IReadOnlyList<Contract.AppBindingToolCapability>>.FromValue(
            value.ApprovedTools.Select(ToContract).ToArray()),
        PendingChanges = DotCraft.Protocol.Optional<IReadOnlyList<Contract.AppBindingCapabilityChange>>.FromValue(
            value.PendingChanges.Select(ToContract).ToArray()),
        FailureReason = OmitIfNull(value.FailureReason)
    };

    private static Contract.AppBindingToolCapability ToContract(AppBindingToolCapability value) => new()
    {
        Namespace = value.Namespace,
        Name = value.Name,
        InputSchema = JsonSerializer.SerializeToElement(value.InputSchema, SessionWireJsonOptions.Default),
        Visibility = DotCraft.Protocol.Optional<IReadOnlyList<string>>.FromValue(value.Visibility),
        Annotations = JsonSerializer.SerializeToElement(value.Annotations, SessionWireJsonOptions.Default),
        Ui = value.Ui is null
            ? default
            : DotCraft.Protocol.Optional<Contract.AppBindingUiCapability?>.FromValue(
                ToContract(value.Ui))
    };

    private static Contract.AppBindingUiCapability ToContract(AppBindingUiCapability value) => new()
    {
        ResourceUri = value.ResourceUri,
        ConnectDomains = DotCraft.Protocol.Optional<IReadOnlyList<string>>.FromValue(value.ConnectDomains),
        ResourceDomains = DotCraft.Protocol.Optional<IReadOnlyList<string>>.FromValue(value.ResourceDomains),
        Permissions = DotCraft.Protocol.Optional<IReadOnlyList<string>>.FromValue(value.Permissions),
        SecurityHash = value.SecurityHash
    };

    private static Contract.AppBindingCapabilityChange ToContract(AppBindingCapabilityChange value) => new()
    {
        Kind = value.Kind,
        Tool = value.Tool,
        Detail = value.Detail
    };

    private static Contract.SocialChannelTarget ToContract(SocialChannelTarget value) => new()
    {
        ChannelName = value.ChannelName,
        AccountId = OmitIfNull(value.AccountId),
        ConversationKind = value.ConversationKind,
        ConversationId = value.ConversationId,
        DeliveryTarget = value.DeliveryTarget,
        DisplayName = OmitIfNull(value.DisplayName),
        BoundBy = value.BoundBy is null
            ? default
            : DotCraft.Protocol.Optional<Contract.SocialChannelBoundBy?>.FromValue(
                new Contract.SocialChannelBoundBy
                {
                    PlatformUserId = value.BoundBy.PlatformUserId,
                    DisplayName = OmitIfNull(value.BoundBy.DisplayName)
                })
    };

    public static Contract.TurnInitiatorContext ToContract(Domain.TurnInitiatorContext value) => new()
    {
        ChannelName = value.ChannelName,
        UserId = value.UserId,
        UserName = value.UserName,
        UserRole = value.UserRole,
        ChannelContext = value.ChannelContext,
        GroupId = value.GroupId
    };

    public static Contract.SessionPlan ToContract(SessionWirePlan value) => new()
    {
        Title = value.Title,
        Overview = value.Overview,
        Content = value.Content,
        Todos = value.Todos.Select(static todo => new Contract.SessionPlanTodo
        {
            Id = todo.Id,
            Content = todo.Content,
            Priority = todo.Priority,
            Status = todo.Status
        }).ToArray()
    };

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize wire enum {typeof(T).Name}.");

    private static DotCraft.Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : DotCraft.Protocol.Optional<T?>.FromValue(value);
}
