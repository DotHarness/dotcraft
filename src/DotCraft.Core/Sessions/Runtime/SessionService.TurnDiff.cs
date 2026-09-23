using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private static void RecordTurnDiff(
        ToolInvocationContext context,
        ToolExecutionResult result,
        TurnExecutionState turnRuntime,
        SessionEventChannel channel)
    {
        // A Remote Tool Host edit reaches the Agent Host only as that call's diff, not as exact file text.
        if (context.ExecutionLocation is { Target: "remote" }
            && FileChangeStructuredContent.IsFileChange(result.StructuredContent))
            turnRuntime.DiffTracker.Invalidate();
        FlushTurnDiff(turnRuntime, channel);
    }

    private static void FlushTurnDiff(TurnExecutionState? turnRuntime, SessionEventChannel channel)
    {
        if (turnRuntime is not null && turnRuntime.DiffTracker.TryTakeSnapshot(out var diff))
            channel.EmitTurnDiffUpdated(diff);
    }
}
