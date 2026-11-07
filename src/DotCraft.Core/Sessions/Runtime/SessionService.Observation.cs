using DotCraft.Tools;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    public async Task<ThreadObservation> ObserveThreadAsync(
        string threadId,
        ThreadObservationView view = ThreadObservationView.Metadata,
        IReadOnlyList<ToolName>? summaryTools = null,
        CancellationToken ct = default)
    {
        var id = NormalizeRequiredThreadId(threadId);
        if (!_runtimeRegistry.TryGetRuntime(id, out var runtime))
            throw new InvalidOperationException($"Thread '{id}' has no loaded runtime.");

        await PrepareObservationStateAsync(runtime, ct);
        return await runtime.Commands.InvokeAsync(commandCt =>
        {
            commandCt.ThrowIfCancellationRequested();
            if (!_runtimeRegistry.IsCurrent(id, runtime) || _runtimeRegistry.IsPendingPermanentDeletion(id))
                throw new InvalidOperationException($"Thread '{id}' runtime is no longer current.");
            var controls = new SessionThread
            {
                Id = id,
                Configuration = runtime.Thread.Configuration
            };
            var observation = runtime.Broker.Observe(
                controls, runtime.Maintenance?.Kind, view, summaryTools ?? [], ct, runtime.Generation);
            observation.Refresh = async (nextView, tools, respond, refreshCt, selection) =>
            {
                await PrepareObservationStateAsync(runtime, refreshCt);
                var response = await runtime.Commands.InvokeAsync(refreshCommandCt =>
                {
                    refreshCommandCt.ThrowIfCancellationRequested();
                    if (!_runtimeRegistry.IsCurrent(id, runtime) || _runtimeRegistry.IsPendingPermanentDeletion(id))
                        throw new InvalidOperationException($"Thread '{id}' runtime is no longer current.");
                    return Task.FromResult(observation.QueueRefresh(
                        new SessionThread
                        {
                            Id = id,
                            Configuration = runtime.Thread.Configuration
                        },
                        runtime.Maintenance?.Kind, nextView, tools, respond, refreshCt, selection));
                }, refreshCt);
                await response;
            };
            return Task.FromResult(observation);
        }, ct);
    }
}
