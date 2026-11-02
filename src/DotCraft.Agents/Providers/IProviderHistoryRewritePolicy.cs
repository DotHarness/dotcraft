using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

public interface IProviderHistoryRewritePolicy
{
    /// <summary>Returns history safe to replay after its prefix changes, without mutating the input.</summary>
    IReadOnlyList<ChatMessage> PrepareForNewPrefix(IReadOnlyList<ChatMessage> messages);
}
