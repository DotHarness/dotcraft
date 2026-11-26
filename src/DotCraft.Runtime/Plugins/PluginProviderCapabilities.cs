using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Runtime;

internal static class PluginProviderCapabilities
{
    public static bool Supports(Type type) =>
        type == typeof(IModelCatalogProvider) || type == typeof(IProviderModelRuntimeResolver)
        || type == typeof(IProviderAuthenticator) || type == typeof(IProviderUsageReader)
        || type == typeof(IProviderNativeHttpRouter);

    public static object Adapt(object target, Type type, PluginInvocation invocation, string backendId) => target switch
    {
        IModelCatalogProvider catalog when type == typeof(IModelCatalogProvider) => new Catalog(catalog, invocation),
        IProviderModelRuntimeResolver runtimeResolver when type == typeof(IProviderModelRuntimeResolver) => new RuntimeResolver(runtimeResolver, invocation),
        IProviderAuthenticator auth when type == typeof(IProviderAuthenticator) => new Authentication(auth, invocation),
        IProviderUsageReader usage when type == typeof(IProviderUsageReader) => new Usage(usage, invocation),
        IProviderNativeHttpRouter router when type == typeof(IProviderNativeHttpRouter) => new PluginNativeHttpRouter(router, invocation, backendId),
        _ => throw new InvalidOperationException("Unsupported provider capability.")
    };

    private static T HostOwned<T>(T value)
    {
        PluginObjectGraphGuard.EnsureHostOwnedGraph(value, "provider capability result");
        return value;
    }

    private sealed class Catalog(IModelCatalogProvider target, PluginInvocation invocation) : IModelCatalogProvider
    {
        private readonly PluginTarget<IModelCatalogProvider> _target = invocation.Capture(target);
        public Task<ModelCatalogResult> FetchModelsAsync(EffectiveModelRuntime runtime, CancellationToken cancellationToken) =>
            invocation.InvokeAsync(new Func<Task<ModelCatalogResult>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, invocation.Stopping);
                return HostOwned(await _target.Value.FetchModelsAsync(runtime, linked.Token).ConfigureAwait(false));
            }));
    }

    private sealed class RuntimeResolver(IProviderModelRuntimeResolver target, PluginInvocation invocation) : IProviderModelRuntimeResolver
    {
        private readonly PluginTarget<IProviderModelRuntimeResolver> _target = invocation.Capture(target);
        public EffectiveModelRuntime ResolveRuntime(EffectiveModelRuntime runtime)
        {
            var result = invocation.Invoke<EffectiveModelRuntime?>(() =>
            {
                EffectiveModelRuntime resolved;
                try { resolved = _target.Value.ResolveRuntime(runtime); }
                catch (ArgumentException) { return null; }
                return HostOwned(resolved with { ModelMetadata = SnapshotMetadata(resolved.ModelMetadata) });
            });
            return result ?? throw new ArgumentException("The provider does not support the selected model.");
        }

        private static ModelCatalogEntry? SnapshotMetadata(ModelCatalogEntry? metadata) =>
            metadata is null
                ? null
                : new ModelCatalogEntry
                {
                    Id = metadata.Id,
                    DisplayName = metadata.DisplayName,
                    OwnedBy = metadata.OwnedBy,
                    CreatedAt = metadata.CreatedAt,
                    Reasoning = metadata.Reasoning is { } reasoning
                        ? reasoning with { SupportedEfforts = reasoning.SupportedEfforts.ToArray() }
                        : null,
                    ContextWindow = metadata.ContextWindow,
                    MaxOutputTokens = metadata.MaxOutputTokens,
                    SupportedProtocols = metadata.SupportedProtocols?.ToArray(),
                    DefaultProtocol = metadata.DefaultProtocol,
                    InputModalities = metadata.InputModalities?.ToArray(),
                    Pricing = metadata.Pricing,
                    SpeedModes = metadata.SpeedModes?.ToArray()
                };
    }

    private sealed class Authentication(IProviderAuthenticator target, PluginInvocation invocation) : IProviderAuthenticator
    {
        private readonly PluginTarget<IProviderAuthenticator> _target = invocation.Capture(target);
        public Task<ProviderAuthenticationStatus> GetStatusAsync(CancellationToken ct) =>
            CallAsync(token => _target.Value.GetStatusAsync(token), ct);
        public Task<ProviderAuthenticationStatus> LoginAsync(string method, IProviderLoginInteraction interaction, CancellationToken ct) =>
            CallAsync(token => _target.Value.LoginAsync(method, new LoginInteraction(interaction), token), ct);
        public Task LogoutAsync(CancellationToken ct) =>
            invocation.InvokeAsync(new Func<Task>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocation.Stopping);
                await _target.Value.LogoutAsync(linked.Token).ConfigureAwait(false);
            }));

        private async Task<ProviderAuthenticationStatus> CallAsync(Func<CancellationToken, Task<ProviderAuthenticationStatus>> call, CancellationToken ct)
        {
            ProviderAuthenticationException? authFailure = null;
            var result = await invocation.InvokeAsync(new Func<Task<ProviderAuthenticationStatus?>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocation.Stopping);
                try { return HostOwned(await call(linked.Token).ConfigureAwait(false)); }
                catch (ProviderAuthenticationException exception)
                {
                    authFailure = new ProviderAuthenticationException(exception.Code, exception.Message);
                    return null;
                }
            })).ConfigureAwait(false);
            if (authFailure is not null) throw authFailure;
            return result!;
        }
    }

    private sealed class LoginInteraction(IProviderLoginInteraction inner) : IProviderLoginInteraction
    {
        public ValueTask NotifyAsync(ProviderLoginEvent notification, CancellationToken ct) =>
            inner.NotifyAsync(HostOwned(notification), ct);
        public Task<string> PromptAsync(ProviderLoginPrompt prompt, CancellationToken ct) =>
            inner.PromptAsync(HostOwned(prompt with { Choices = prompt.Choices?.ToArray() }), ct);
    }

    private sealed class Usage(IProviderUsageReader target, PluginInvocation invocation) : IProviderUsageReader
    {
        private readonly PluginTarget<IProviderUsageReader> _target = invocation.Capture(target);
        public Task<ProviderUsageSnapshot?> ReadAsync(CancellationToken ct) =>
            invocation.InvokeAsync(new Func<Task<ProviderUsageSnapshot?>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocation.Stopping);
                return HostOwned(await _target.Value.ReadAsync(linked.Token).ConfigureAwait(false));
            }));
    }
}
