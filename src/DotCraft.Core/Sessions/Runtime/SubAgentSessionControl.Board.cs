using DotCraft.Tools;

namespace DotCraft.Sessions;

public static partial class SubAgentSessionControl
{
    internal static async Task<SessionThread> ResolveBoardAgentAsync(
        SubAgentSessionContext context, string target, CancellationToken ct)
    {
        var resolved = await ResolveAgentTargetAsync(context.SessionService, context.ParentThread,
            context.ParentThread.Source.SubAgent?.RootThreadId ?? context.ParentThread.Id, target, requireOpen: true, ct);
        if (resolved.Thread.Ephemeral || !IsNativeRuntime(resolved.Thread)
            || ToolPlanningThreadClassifier.Classify(resolved.Thread) == ToolPlanningThreadKind.Internal)
            throw new BoardException("board_unavailable", "Message boards require a persistent native Agent.");
        return resolved.Thread;
    }
}
