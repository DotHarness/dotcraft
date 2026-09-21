using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class TurnInterruption
{
    internal const string Kind = "turn_aborted";
    private const string TurnIdKey = "dotcraft.interrupted_turn";

    public static ChatMessage Create(string turnId, ThreadContextCarrier carrier)
    {
        var message = ThreadContextItems.Create(carrier, Kind,
            "<turn_aborted>\nThe previous turn was interrupted on purpose. If any tools or commands were aborted, they may have partially executed.\n</turn_aborted>");
        message.AdditionalProperties![TurnIdKey] = turnId;
        return message;
    }

    public static string? GetTurnId(ChatMessage message)
    {
        if (!ThreadContextItems.IsKind(message, Kind)
            || message.AdditionalProperties?.TryGetValue(TurnIdKey, out var value) != true)
            return null;
        return value?.ToString();
    }
}
