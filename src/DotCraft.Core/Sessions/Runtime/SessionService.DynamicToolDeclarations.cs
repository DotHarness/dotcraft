using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService : IThreadDynamicToolDeclarationService
{
    public async Task UpdateThreadDynamicToolsAsync(
        string threadId,
        IReadOnlyList<ThreadDynamicToolDeclaration> declarations,
        Func<bool> canApply,
        Action updateLease,
        CancellationToken ct = default)
    {
        var captured = ThreadDynamicToolDeclaration.Freeze(declarations);
        while (true)
        {
            var pending = await InvokeThreadCommandAsync<Task<TurnExecutionResources>?>(threadId, async commandCt =>
            {
                var runtime = GetInputRuntime(threadId);
                if (!canApply())
                    return null;

                var thread = runtime.Thread;
                var active = thread.Turns.LastOrDefault(static turn =>
                    turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval or TurnStatus.WaitingInput);
                if (active is not null && runtime.TryGetTurn(active.Id, out var turnRuntime)
                    && turnRuntime.Context is { Resources.IsCompleted: false } context)
                    return context.Resources;

                using var agentLock = await AcquireThreadAgentLockAsync(threadId, commandCt).ConfigureAwait(false);
                if (!canApply())
                    return null;

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
                return null;
            }, ct).ConfigureAwait(false);

            if (pending is null)
                return;

            await ObserveTurnResourcesAsync(pending).WaitAsync(ct).ConfigureAwait(false);
        }
    }
}
