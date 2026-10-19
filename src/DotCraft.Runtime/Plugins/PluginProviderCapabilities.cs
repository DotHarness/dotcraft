using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Runtime;

internal static class PluginProviderCapabilities
{
    public static bool Supports(Type type) =>
        type == typeof(IModelCatalogProvider) || type == typeof(IProviderAuthenticator)
        || type == typeof(IProviderUsageReader);

    public static object Adapt(object target, Type type, PluginInvocation invocation) => target switch
    {
        IModelCatalogProvider catalog when type == typeof(IModelCatalogProvider) => new Catalog(catalog, invocation),
        IProviderAuthenticator auth when type == typeof(IProviderAuthenticator) => new Authentication(auth, invocation),
        IProviderUsageReader usage when type == typeof(IProviderUsageReader) => new Usage(usage, invocation),
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

        private Task<ProviderAuthenticationStatus> CallAsync(Func<CancellationToken, Task<ProviderAuthenticationStatus>> call, CancellationToken ct) =>
            invocation.InvokeAsync(new Func<Task<ProviderAuthenticationStatus>>(async () =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, invocation.Stopping);
                return HostOwned(await call(linked.Token).ConfigureAwait(false));
            }));
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
