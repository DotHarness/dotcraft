using System.Text.Json;
using DotCraft.Protocol;
using Contract = DotCraft.Protocol.AppServer;
using DotCraft.Sessions;
using DotCraft.Sessions.Wire;

namespace DotCraft.AppServer;

/// <summary>
/// Projects between executable contract DTOs and the existing AppServer domain wire models while
/// retaining extension properties that are not yet modeled by the initial contract slice.
/// </summary>
public static class AppServerContractMapper
{



    public static Contract.TokenUsageInfo ToContract(TokenUsageInfo value) => new()
    {
        InputTokens = value.InputTokens,
        OutputTokens = value.OutputTokens,
        CachedInputTokens = value.CachedInputTokens,
        CacheWriteInputTokens = value.CacheWriteInputTokens,
        FreshInputTokens = value.FreshInputTokens,
        NonCachedInputTokens = value.NonCachedInputTokens,
        ReasoningOutputTokens = value.ReasoningOutputTokens,
        LlmCallCount = value.LlmCallCount,
        CacheHitRate = value.CacheHitRate,
        TotalTokens = value.TotalTokens
    };

    /// <summary>Projects the complete public Session Wire thread shape into Contracts.</summary>
    public static Contract.SessionThread ToContract(SessionWireThread value) => new()
    {
        Id = value.Id,
        SessionId = value.SessionId,
        WorkspacePath = value.WorkspacePath,
        Cwd = value.Cwd,
        RuntimeWorkspaceRoots = value.RuntimeWorkspaceRoots,
        EffectiveWorkspacePath = value.EffectiveWorkspacePath,
        Path = value.Path,
        ForkedFromId = value.ForkedFromId,
        ParentThreadId = value.ParentThreadId,
        Ephemeral = value.Ephemeral,
        Worktree = value.Worktree is null ? null : WorktreeContractMapper.ToContract(value.Worktree),
        UserId = value.UserId,
        OriginChannel = value.OriginChannel,
        ChannelContext = value.ChannelContext,
        DisplayName = value.DisplayName,
        Source = ThreadContractMapper.ToContract(value.Source),
        Status = WireString(value.Status),
        CreatedAt = value.CreatedAt,
        LastActiveAt = value.LastActiveAt,
        HistoryMode = WireString(value.HistoryMode),
        Configuration = value.Configuration is null ? null : ThreadConfigurationContractMapper.ToContract(value.Configuration),
        Metadata = value.Metadata,
        Runtime = ToContract(value.Runtime),
        QueuedInputs = TurnContractMapper.ToContract(value.QueuedInputs),
        Goal = value.Goal is null ? null : ThreadContractMapper.ToContract(value.Goal),
        AppBindings = value.AppBindings?.Select(ThreadContractMapper.ToContract).ToArray(),
        OriginApp = value.OriginApp is null ? null : ThreadContractMapper.ToContract(value.OriginApp),
        OriginPresentation = value.OriginPresentation is null ? null : ThreadContractMapper.ToContract(value.OriginPresentation),
        Turns = value.Turns?.Select(ToContract).ToArray(),
        Plan = value.Plan is null ? null : ThreadContractMapper.ToContract(value.Plan),
        ContextUsage = value.ContextUsage is null ? null : ThreadContractMapper.ToContract(value.ContextUsage)
    };

    /// <summary>Projects the complete public Session Wire turn shape into Contracts.</summary>
    public static Contract.SessionTurn ToContract(SessionWireTurn value) => new()
    {
        Id = value.Id,
        ThreadId = value.ThreadId,
        Status = WireString(value.Status),
        StartedAt = value.StartedAt,
        CompletedAt = value.CompletedAt,
        TokenUsage = value.TokenUsage is null ? null : ToContract(value.TokenUsage),
        Error = value.Error,
        OriginChannel = value.OriginChannel,
        Initiator = value.Initiator is null ? null : ThreadContractMapper.ToContract(value.Initiator),
        Items = value.Items?.Select(ToContract).ToArray()
    };

    private static Contract.ThreadRuntimeState ToContract(SessionRuntimeSnapshot value) => new()
    {
        Running = value.Running,
        WaitingOnApproval = value.WaitingOnApproval,
        WaitingOnInput = value.WaitingOnInput,
        WaitingOnPlanConfirmation = value.WaitingOnPlanConfirmation,
        Busy = value.Busy,
        MaintenanceKind = value.MaintenanceKind is null
            ? default
            : Optional<string?>.FromValue(value.MaintenanceKind)
    };

    /// <summary>Projects the complete public Session Wire item shape into Contracts.</summary>
    public static Contract.SessionItem ToContract(SessionWireItem value) => new()
    {
        Id = value.Id,
        TurnId = value.TurnId,
        Type = WireString(value.Type),
        Status = WireString(value.Status),
        CreatedAt = value.CreatedAt,
        CompletedAt = value.CompletedAt,
        PayloadKind = value.PayloadKind,
        Payload = value.Payload is null
            ? default
            : Optional<JsonElement?>.FromValue(JsonSerializer.SerializeToElement(
                value.Payload,
                value.Payload.GetType(),
                SessionWireJsonOptions.Default)),
        McpApp = value.McpApp is null ? null : new Contract.McpAppViewHint
        {
            Available = value.McpApp.Available
        }
    };

    public static Contract.ThreadGoal ToContract(ThreadGoalSnapshot value) =>
        ThreadContractMapper.ToContract(value);

    private static string WireString<T>(T value) where T : struct, Enum =>
        JsonSerializer.SerializeToElement(value, SessionWireJsonOptions.Default).GetString()
        ?? throw new JsonException($"Could not serialize wire enum {typeof(T).Name}.");

}
