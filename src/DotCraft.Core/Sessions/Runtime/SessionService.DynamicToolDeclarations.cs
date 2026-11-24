using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService : IThreadDynamicToolDeclarationService
{
    public Task UpdateThreadDynamicToolsAsync(
        string threadId,
        IReadOnlyList<ThreadDynamicToolDeclaration> declarations,
        Func<bool> canApply,
        Action updateLease,
        CancellationToken ct = default)
    {
        var captured = ThreadDynamicToolDeclaration.Freeze(declarations);
        return InvokeThreadCommandAsync(threadId, async commandCt =>
        {
            var runtime = GetInputRuntime(threadId);
            var thread = runtime.Thread;
            var active = thread.Turns.LastOrDefault(static turn =>
                turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
            if (active is not null && runtime.TryGetTurn(active.Id, out var turnRuntime)
                && turnRuntime.Context is { } context)
                await ObserveTurnResourcesAsync(context.Resources).ConfigureAwait(false);

            using (await AcquireThreadAgentLockAsync(threadId, commandCt).ConfigureAwait(false))
            {
                if (!canApply())
                    return;

                if (!ThreadDynamicToolDeclaration.Match(thread.DynamicToolDeclarations, captured))
                {
                    if (thread.Ephemeral)
                    {
                        thread.DynamicToolDeclarations = captured;
                    }
                    else
                    {
                        await persistence.CommitThreadDynamicToolDeclarationsAsync(thread, captured, commandCt).ConfigureAwait(false);
                        SetMaterializedIfCurrent(thread, materialized: true);
                    }
                }

                updateLease();
                runtime.MarkToolSnapshotDirty();
            }
        }, ct);
    }
}
