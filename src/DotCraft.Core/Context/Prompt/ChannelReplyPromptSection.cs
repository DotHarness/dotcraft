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
- Deliver answers, task results, and necessary updates for chat-originated requests with `SendChannelMessage` before ending the turn. Commentary, final text, and generated images remain internal to the session, so attach images and files the chat should see.
- Reply to local requests on the requesting surface. Send to a chat when the user asks.
""";
}
