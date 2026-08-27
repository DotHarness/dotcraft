using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal enum AgentInstructionsHistoryChange
{
    None,
    Added,
    Replaced,
    Removed
}

/// <summary>
/// Reconciles the single provider-neutral AGENTS.md item at the front of model history.
/// </summary>
internal static class AgentInstructionsHistory
{
    public const string Kind = "agents_md.instructions";

    public static AgentInstructionsHistoryChange Reconcile(
        IList<ChatMessage> history,
        string desiredContent)
    {
        ArgumentNullException.ThrowIfNull(history);

        var index = -1;
        for (var i = 0; i < history.Count; i++)
        {
            if (ThreadContextItems.IsKind(history[i], Kind))
            {
                index = i;
                break;
            }
        }

        if (desiredContent.Length == 0)
        {
            if (index < 0)
                return AgentInstructionsHistoryChange.None;
            history.RemoveAt(index);
            return AgentInstructionsHistoryChange.Removed;
        }

        var desired = Create(desiredContent);
        if (index < 0)
        {
            history.Insert(0, desired);
            return AgentInstructionsHistoryChange.Added;
        }

        var unchanged = history[index].Role == ChatRole.User
                        && string.Equals(history[index].Text, desiredContent, StringComparison.Ordinal);
        if (unchanged)
            return AgentInstructionsHistoryChange.None;

        history[index] = desired;
        return AgentInstructionsHistoryChange.Replaced;
    }

    public static bool IsInstructions(ChatMessage message) =>
        ThreadContextItems.IsKind(message, Kind);

    private static ChatMessage Create(string content) =>
        new(ChatRole.User, content)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                [ThreadContextItems.KindMetadataKey] = Kind
            }
        };

}
