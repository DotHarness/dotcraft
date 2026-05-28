using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionMessageTruncator
{
    public static List<ChatMessage> TruncateOldestGroups(IReadOnlyList<ChatMessage> messages)
    {
        var groups = MessageGrouper.GroupByApiRound(messages);
        if (groups.Count <= 1)
            return [];

        var dropCount = Math.Clamp((int)Math.Ceiling(groups.Count * 0.2), 1, groups.Count - 1);
        var truncated = groups
            .Skip(dropCount)
            .SelectMany(group => group.Messages)
            .ToList();
        return MessageGrouper.EnsurePairing(truncated);
    }
}
