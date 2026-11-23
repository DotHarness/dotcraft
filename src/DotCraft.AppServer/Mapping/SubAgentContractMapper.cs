using Contract = DotCraft.Protocol.AppServer;
using Domain = DotCraft.Sessions;

namespace DotCraft.AppServer;

/// <summary>Maps SubAgent runtime and persistence models to the public AppServer contract.</summary>
internal static class SubAgentContractMapper
{
    public static Contract.SubAgentControlResult ToContract(Domain.SubAgentControlResult value) => new()
    {
        AgentPath = OmitIfNull(value.AgentPath),
        TaskName = OmitIfNull(value.TaskName),
        Status = value.Status,
        Message = OptionalNull(value.Message),
        AgentNickname = OptionalNull(value.AgentNickname),
        AgentRole = OptionalNull(value.AgentRole)
    };

    public static Contract.ThreadSpawnEdge ToContract(Domain.ThreadSpawnEdge value) => new()
    {
        ParentThreadId = value.ParentThreadId,
        ChildThreadId = value.ChildThreadId,
        ParentTurnId = OmitIfNull(value.ParentTurnId),
        Depth = value.Depth,
        AgentPath = OmitIfNull(value.AgentPath),
        TaskName = OmitIfNull(value.TaskName),
        AgentNickname = OmitIfNull(value.AgentNickname),
        AgentRole = OmitIfNull(value.AgentRole),
        Status = value.Status,
        CreatedAt = value.CreatedAt,
        UpdatedAt = value.UpdatedAt
    };

    public static T? Read<T>(Protocol.Optional<T> value) =>
        value.IsSet ? value.Value : default;

    private static Protocol.Optional<T?> OmitIfNull<T>(T? value) =>
        value is null ? default : Protocol.Optional<T?>.FromValue(value);

    private static Protocol.Optional<string?> OptionalNull(string? value) =>
        Protocol.Optional<string?>.FromValue(value);
}
