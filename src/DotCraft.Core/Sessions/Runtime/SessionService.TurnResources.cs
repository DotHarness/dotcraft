namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task<TurnExecutionResources> CaptureTurnExecutionResourcesAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        if (!_hasExplicitDefaultAgent || _forcePerThreadAgents)
            await EnsurePerThreadAgentIfMissingAsync(runtime.Thread.Id, runtime.Thread, ct).ConfigureAwait(false);
        TurnExecutionResources resources;
        using (await AcquireThreadAgentLockAsync(runtime.Thread.Id, ct).ConfigureAwait(false))
        {
            if (!_runtimeRegistry.IsCurrent(runtime.Thread.Id, runtime))
                throw new InvalidOperationException($"Thread '{runtime.Thread.Id}' runtime was replaced during Turn admission.");
            resources = new(runtime.Agent ?? DefaultAgent, runtime.LatestToolSnapshot);
        }
        return resources;
    }

    private ValueTask PrepareRemoteTurnAsync(string threadId, DotCraft.Tools.EffectiveToolSnapshot? snapshot,
        string mode, CancellationToken ct) => snapshot is not null && agentFactory?.RemoteToolHostClient is { } remote
            ? remote.PrepareTurnAsync(threadId, snapshot, mode, ct) : ValueTask.CompletedTask;
}
