using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class AgentInstructionsHistory
{
    public const string Kind = "agents_md.instructions";

    public static bool IsInstructions(ChatMessage message) => ThreadContextItems.IsKind(message, Kind);

    public static ChatMessage Create(string content) => new(ChatRole.User, content)
    {
        AdditionalProperties = new AdditionalPropertiesDictionary { [ThreadContextItems.KindMetadataKey] = Kind }
    };
}
