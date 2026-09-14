using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

internal static class CompactionSummaryMessage
{
    public static ChatMessage Create(string formattedSummary) => new(ChatRole.User, formattedSummary);
}
