using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Http;

namespace DotCraft.ModelService;

public sealed class FileModelServiceAccess : IModelServiceAccess
{
    private readonly string _stateDirectory;
    private readonly ModelServiceClientStore _clients;
    private readonly OpenAIAuthManager _authentication;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<EffectiveModelRuntime, OpenRouterProvider> _openRouter = new();

    public FileModelServiceAccess(string stateDirectory, OpenAIAuthManager? authentication = null)
    {
        _stateDirectory = Path.GetFullPath(stateDirectory);
        _clients = new ModelServiceClientStore(_stateDirectory);
        _authentication = authentication ?? new OpenAIAuthManager(
            new OpenAITokenStore(Path.Combine(_stateDirectory, "credentials")));
    }

    public ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var client = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? _clients.Authenticate(authorization[7..]) : null;
        return ValueTask.FromResult(client is null ? null : new ModelServiceCaller(client.Id, client));
    }

    public async Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken)
    {
        var config = LoadConfiguration(_stateDirectory);
        var client = (ModelServiceClient)caller.State!;
        var providers = new List<ModelServiceProvider>();
        foreach (var id in client.Providers)
        {
            if (!config.Providers.ContainsKey(id))
                continue;
            var runtime = ModelProviderResolver.ResolveProvider(config, id);
            if (runtime.BackendId == OpenRouterBackend.Id)
            {
                var backend = _openRouter.GetOrAdd(runtime, value => new OpenRouterProvider(new ModelBackendContext(value,
                    new ProviderCredentialStore(_stateDirectory, OpenRouterBackend.Id, id), CancellationToken.None)));
                var resolved = backend.ResolveRuntime(runtime);
                var ready = await backend.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                providers.Add(new ModelServiceProvider(new RemoteModelProvider(id, runtime.DisplayName,
                    resolved.ConfiguredProtocol ?? "auto", resolved.EndPoint, resolved.AuthMethod, false, false, ready,
                    runtime.MaxOutputTokens, runtime.NetworkTimeoutSeconds, BackendId: OpenRouterBackend.Id,
                    BackendSettingsJson: runtime.BackendSettingsJson), resolved, Backend: backend));
                continue;
            }
            var status = runtime.IsChatGptOAuth ? _authentication.GetStatus() : null;
            var authentication = status is null
                ? new ProviderAuthenticationStatus(!string.IsNullOrWhiteSpace(runtime.ApiKey))
                : new ProviderAuthenticationStatus(status.LoggedIn, status.AccountId,
                    PlanType: status.PlanType, Email: status.Email, LastRefresh: status.LastRefresh,
                    AccessTokenExpiresAt: status.AccessTokenExpiresAt);
            providers.Add(new ModelServiceProvider(
                new RemoteModelProvider(id, runtime.DisplayName, runtime.Protocol, runtime.EndPoint, runtime.AuthMethod,
                    runtime.SupportsImageGeneration, runtime.SupportsFreeformTools, authentication, runtime.MaxOutputTokens, runtime.NetworkTimeoutSeconds),
                runtime, runtime.IsChatGptOAuth ? _authentication : null));
        }
        return providers;
    }

    public ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IDisposable?>(null);

    public static AppConfig LoadConfiguration(string stateDirectory)
    {
        var path = Path.Combine(stateDirectory, "config.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"Model service configuration is missing: {path}");
        var config = AppConfig.Load(path);
        config.GlobalConfigPath = path;
        if (config.ModelService is not null)
            throw new InvalidOperationException("A model service must configure upstream providers directly.");
        foreach (var id in config.Providers.Keys)
        {
            var runtime = ModelProviderResolver.ResolveProvider(config, id);
            if (runtime.BackendId == OpenRouterBackend.Id)
            {
                _ = OpenRouterModelCatalog.ApiRoot(runtime.EndPoint);
                continue;
            }
            if (runtime.BackendId is not null)
                throw new InvalidOperationException("Local plugin backends are not supported by the model service.");
            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException($"Provider '{id}' requires an HTTP endpoint.");
            if (!runtime.IsChatGptOAuth && string.IsNullOrWhiteSpace(runtime.ApiKey))
                throw new InvalidOperationException($"Provider '{id}' requires an API key.");
        }
        return config;
    }
}
