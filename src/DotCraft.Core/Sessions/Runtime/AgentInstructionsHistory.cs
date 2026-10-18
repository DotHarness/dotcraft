using System.Text.Json.Nodes;
using DotCraft.Context.WorldState;
using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class AgentInstructionsHistory
{
    public const string Kind = "agents_md.instructions";
    private const string BaselineKey = "dotcraft.agents_md.baseline";

    public static bool IsInstructions(ChatMessage message) => ThreadContextItems.IsKind(message, Kind);

    public static ChatMessage Create(string content) => new(ChatRole.User, content)
    {
        AdditionalProperties = new AdditionalPropertiesDictionary { [ThreadContextItems.KindMetadataKey] = Kind }
    };

    public static void InstallBaseline(IList<ChatMessage> history, string content)
    {
        for (var index = history.Count - 1; index >= 0; index--)
        {
            if (IsInstructions(history[index]))
                history.RemoveAt(index);
        }
        if (content.Length == 0)
            return;
        var message = Create(content);
        message.AdditionalProperties![BaselineKey] = true;
        history.Insert(0, message);
    }

    public static JsonNode? TryGetInstalledBaseline(IReadOnlyList<ChatMessage> history)
    {
        var message = history.LastOrDefault(IsInstructions);
        return message?.AdditionalProperties?.ContainsKey(BaselineKey) == true
            ? AgentsMdSection.CreateSnapshot(message.Text)
            : null;
    }
}
