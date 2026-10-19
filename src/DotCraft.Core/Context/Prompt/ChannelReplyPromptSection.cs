using DotCraft.Contributions;

namespace DotCraft.Context;

internal static class ChannelReplyPromptSection
{
    internal static string? Build(SystemPromptSectionContext context) =>
        context.IsToolAvailable("SendChannelMessage") ? Instructions : null;

    private const string Instructions =
"""
## Channel Replies
- Choose the reply destination from the current Request Source and the user's delivery instructions.
- Deliver answers, task results, and necessary updates for chat-originated requests with `SendChannelMessage` before ending the turn. Commentary and final text remain internal to the session.
- Reply to local requests on the requesting surface. Send to a chat when the user asks.
""";
}
