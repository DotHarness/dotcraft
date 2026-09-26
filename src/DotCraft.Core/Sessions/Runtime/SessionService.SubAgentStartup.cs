using Microsoft.Extensions.AI;

namespace DotCraft.Sessions;

public sealed partial class SessionService : ISubAgentStartupLifecycleService, ISubAgentInitialInputService
{
    IAsyncEnumerable<SessionEvent> ISubAgentInitialInputService.SubmitSubAgentInitialInput(
        string childThreadId, IList<AIContent> content, CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetRuntime(childThreadId, out var runtime))
            throw new InvalidOperationException($"Thread '{childThreadId}' has no active runtime.");
        var channel = runtime.Commands.InvokeAsync(
            _ => Task.FromResult(StartTurn(runtime, content, null, null, null, ct)), ct).GetAwaiter().GetResult();
        // Keep observing durable admission and terminal cleanup when the caller cancels execution.
        return channel.ReadAllAsync(CancellationToken.None);
    }

    Task ISubAgentStartupLifecycleService.PersistPreparedSubAgentAsync(
        string parentThreadId, string childThreadId, CancellationToken ct) =>
        InvokeThreadCommandAsync(childThreadId, async token =>
        {
            var child = await GetOrLoadThreadAsync(childThreadId, token);
            if (child.Source.SubAgent?.ParentThreadId != parentThreadId)
                throw new InvalidOperationException("Subagent preparation requires the owning parent thread.");
            await PersistThreadWithMaterializationAsync(child, token);
        }, ct);

    async Task ISubAgentStartupLifecycleService.DiscardFailedSubAgentAsync(
        string parentThreadId, string childThreadId, CancellationToken ct)
    {
        if (!_runtimeRegistry.TryGetThread(childThreadId, out _)
            && await Persistence.LoadThreadAsync(childThreadId, ct) == null) return;
        var deleteOrder = await InvokeThreadCommandAsync(childThreadId,
            token => ThreadLifecycle.PrepareFailedSubAgentDeletionAsync(parentThreadId, childThreadId, token), ct);
        await ThreadLifecycle.ExecutePermanentDeletionAsync(deleteOrder, ct);
        _sessionApprovalScopes.RemoveThread(childThreadId);
        McpAppTransientContexts?.ClearThread(childThreadId);
    }
}
