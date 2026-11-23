using DotCraft.Configuration;

namespace DotCraft.Sessions;

public sealed partial class SessionService
{
    private async Task RefreshRuntimeResourcesAsync(CancellationToken ct)
    {
        foreach (var refresher in runtimeRefreshers ?? [])
            await refresher.RefreshAsync(ct).ConfigureAwait(false);
    }

    private async Task<TurnExecutionResources> CaptureTurnExecutionResourcesAsync(ThreadRuntime runtime, CancellationToken ct)
    {
        await RefreshRuntimeResourcesAsync(ct).ConfigureAwait(false);
        var context = agentFactory.RuntimeContext;
        var config = _appConfigMonitor?.Current ?? context.Config;
        var providerId = runtime.Thread.Configuration?.ProviderId;
        providerId = string.IsNullOrWhiteSpace(providerId) ? config.ProviderId : providerId;
        var usesSubscription = providerId is not null
            && config.Providers.TryGetValue(providerId.Trim(), out var provider)
            && string.IsNullOrWhiteSpace(provider.BackendId)
            && ModelProviderAuthMethods.IsChatGptOAuth(provider.AuthMethod);
        if (usesSubscription && config.ModelService is null)
        {
            await ModelProviderCatalog.FetchAsync(config, context.ChatClientRegistry.RuntimeResolver, providerId,
                cancellationToken: ct).ConfigureAwait(false);
        }

        var modelRuntime = usesSubscription
            ? context.ChatClientRegistry.ResolveMainRuntime(config, providerId, runtime.Thread.Configuration?.Model)
            : null;
        if (!_hasExplicitDefaultAgent || _forcePerThreadAgents || runtime.ToolSnapshotDirty
            || (modelRuntime is not null && runtime.Agent is not null))
            await EnsurePerThreadAgentIfMissingAsync(runtime.Thread.Id, runtime.Thread, ct, modelRuntime).ConfigureAwait(false);
        TurnExecutionResources resources;
        using (await AcquireThreadAgentLockAsync(runtime.Thread.Id, ct).ConfigureAwait(false))
        {
            if (!_runtimeRegistry.IsCurrent(runtime.Thread.Id, runtime))
                throw new InvalidOperationException($"Thread '{runtime.Thread.Id}' runtime was replaced during Turn admission.");
            resources = new(
                runtime.Agent ?? DefaultAgent,
                runtime.LatestToolSnapshot,
                runtime.ToolLocation,
                runtime.SubAgentModels,
                modelRuntime);
        }
        return resources;
    }

    private ValueTask PrepareRemoteTurnAsync(string threadId, DotCraft.Tools.EffectiveToolSnapshot? snapshot,
        string mode, CancellationToken ct) => snapshot is not null && agentFactory?.RemoteToolHostClient is { } remote
            ? remote.PrepareTurnAsync(threadId, snapshot, mode, ct) : ValueTask.CompletedTask;
}
