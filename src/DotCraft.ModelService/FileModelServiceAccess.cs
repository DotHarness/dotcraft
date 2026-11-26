using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using DotCraft.Contributions;
using Microsoft.AspNetCore.Http;

namespace DotCraft.ModelService;

public sealed class FileModelServiceAccess : IModelServiceAccess, IDisposable
{
    private readonly string _stateDirectory;
    private readonly ModelServiceClientStore _clients;
    private readonly OpenAIAuthManager _authentication;
    private readonly ModelBackendResolver _backends;
    private readonly bool _ownsBackends;
    private readonly Dictionary<(string Backend, string Provider), string> _credentialRevisions = new();

    public FileModelServiceAccess(string stateDirectory, OpenAIAuthManager? authentication = null, ModelBackendResolver? backends = null)
    {
        _stateDirectory = Path.GetFullPath(stateDirectory);
        _clients = new ModelServiceClientStore(_stateDirectory);
        _authentication = authentication ?? new OpenAIAuthManager(
            new OpenAITokenStore(Path.Combine(_stateDirectory, "credentials")));
        _backends = backends ?? CreateDefaultResolver(_stateDirectory);
        _ownsBackends = backends is null;
    }

    public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var client = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? _clients.Authenticate(authorization[7..]) : null;
        return ValueTask.FromResult(client is null ? null : new ModelServiceCaller(client.Id, client));
    }

    public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken)
    {
        var config = ReadConfiguration(_stateDirectory);
        var client = (ModelServiceClient)caller.State!;
        var providers = new List<ModelServiceProvider>();
        foreach (var id in client.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Resolve(config, id) is { } provider)
                providers.Add(provider);
        }
        return Task.FromResult<IReadOnlyList<ModelServiceProvider>>(providers);
    }

    public Task<ModelServiceProvider?> GetProviderAsync(ModelServiceCaller caller, string providerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = (ModelServiceClient)caller.State!;
        if (!client.Providers.Contains(providerId, StringComparer.Ordinal))
            return Task.FromResult<ModelServiceProvider?>(null);

        var config = ReadConfiguration(_stateDirectory);
        var provider = Resolve(config, providerId);
        if (provider is null && config.Providers.ContainsKey(providerId))
            throw new ModelServiceException("backend_unavailable", "The requested backend is unavailable.",
                StatusCodes.Status503ServiceUnavailable);

        return Task.FromResult(provider);
    }

    private ModelServiceProvider? Resolve(AppConfig config, string id)
    {
        if (!config.Providers.ContainsKey(id))
            return null;
        var runtime = ModelProviderResolver.ResolveProvider(config, id);
        if (runtime.BackendId is not null)
        {
            RefreshCredentialRevision(runtime);
            return ModelServiceProvider.ResolveBackend(runtime, _backends);
        }
        var status = runtime.IsChatGptOAuth ? _authentication.GetStatus() : null;
        var authentication = status is null
            ? new ProviderAuthenticationStatus(!string.IsNullOrWhiteSpace(runtime.ApiKey))
            : new ProviderAuthenticationStatus(status.LoggedIn, status.AccountId,
                PlanType: status.PlanType, Email: status.Email, LastRefresh: status.LastRefresh,
                AccessTokenExpiresAt: status.AccessTokenExpiresAt);
        return new ModelServiceProvider(
            new RemoteModelProvider(id, runtime.DisplayName, runtime.Protocol, runtime.EndPoint, runtime.AuthMethod,
                runtime.SupportsImageGeneration, runtime.SupportsFreeformTools, authentication, runtime.MaxOutputTokens, runtime.NetworkTimeoutSeconds),
            runtime, runtime.IsChatGptOAuth ? _authentication : null);
    }

    private void RefreshCredentialRevision(EffectiveModelRuntime runtime)
    {
        var key = (runtime.BackendId!, runtime.ProviderId.ToLowerInvariant());
        lock (_credentialRevisions)
        {
            var version = new ProviderCredentialStore(_stateDirectory, key.Item1, key.Item2).ReadRevision();
            if (!_credentialRevisions.TryGetValue(key, out var previous) || version != previous)
                _backends.InvalidateAuthentication(runtime);
            _credentialRevisions[key] = version;
        }
    }

    public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IDisposable?>(null);

    public static AppConfig LoadConfiguration(string stateDirectory, ModelBackendResolver? backends = null)
    {
        var config = ReadConfiguration(stateDirectory);
        using var defaultResolver = backends is null ? CreateDefaultResolver(stateDirectory) : null;
        backends ??= defaultResolver!;
        foreach (var id in config.Providers.Keys.Where(id => ModelProviderResolver.IsBackendProvider(config, id)))
        {
            var runtime = ModelProviderResolver.ResolveProvider(config, id);
            var (_, backend, _) = backends.ResolveBackend(runtime);
            if (backend.GetService(typeof(IProviderNativeHttpRouter)) is not IProviderNativeHttpRouter)
                throw new InvalidOperationException($"Provider '{id}' does not support inference through Model Service.");
        }
        return config;
    }

    public static AppConfig ReadConfiguration(string stateDirectory)
    {
        var config = ReadStateConfiguration(stateDirectory);
        foreach (var id in config.Providers.Keys)
        {
            var runtime = ModelProviderResolver.ResolveProvider(config, id);
            if (runtime.BackendId is not null)
                continue;
            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException($"Provider '{id}' requires an HTTP endpoint.");
            if (!runtime.IsChatGptOAuth && string.IsNullOrWhiteSpace(runtime.ApiKey))
                throw new InvalidOperationException($"Provider '{id}' requires an API key.");
        }
        return config;
    }

    public static AppConfig ReadStateConfiguration(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, "config.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Model service configuration is missing: {path}");
        var config = AppConfig.Load(path);
        config.GlobalConfigPath = path;
        if (config.ModelService is not null)
            throw new InvalidOperationException("A model service must configure upstream providers directly.");
        return config;
    }

    private static ModelBackendResolver CreateDefaultResolver(string stateDirectory) =>
        new(new ModelProviderRegistry([]), new ContributionRegistry(), Path.GetFullPath(stateDirectory), [new OpenRouterBackend()]);

    public void Dispose()
    {
        if (_ownsBackends)
            _backends.Dispose();
    }
}
