using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

internal static class SideChatBoundary
{
    public const string Kind = "side_chat_boundary";

    private const string Text =
        """
        ## Side Chat

        You are now in a side chat, a temporary conversation beside the main thread. Everything above this point was copied from the main thread as reference context, and the main thread may still be working on its task.

        - Act only on messages sent after this point. Requests, plans, and tool calls above it belong to the main thread, so do not continue or complete them here.
        - Answer questions and explore without changing anything, such as reading files, searching, or running checks that leave the workspace untouched.
        - Change files, Git state, or configuration only when the user explicitly asks for that change in this side chat, and keep it minimal.
        - Sub-agents are unavailable in a side chat.
        """;

    public static bool Reconcile(SessionThread thread, IList<ChatMessage> history, ThreadContextCarrier carrier)
    {
        if (CacheSharingFork.PurposeOf(thread) != ThreadForkPurpose.SideChat
            || ThreadContextItems.FindLast(history, Kind) != null)
        {
            return false;
        }

        history.Add(ThreadContextItems.Create(carrier, Kind, Text));
        return true;
    }
}
