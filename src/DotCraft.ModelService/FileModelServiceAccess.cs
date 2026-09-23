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

    public Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken)
    {
        var config = LoadConfiguration(_stateDirectory);
        var client = (ModelServiceClient)caller.State!;
        var providers = new List<ModelServiceProvider>();
        foreach (var id in client.Providers)
        {
            if (!config.Providers.ContainsKey(id))
                continue;
            var runtime = ModelProviderResolver.ResolveProvider(config, id);
            var status = runtime.IsChatGptOAuth ? _authentication.GetStatus() : null;
            var authentication = status is null
                ? new ProviderAuthenticationStatus(!string.IsNullOrWhiteSpace(runtime.ApiKey))
                : new ProviderAuthenticationStatus(status.LoggedIn, status.AccountId,
                    PlanType: status.PlanType, Email: status.Email, LastRefresh: status.LastRefresh,
                    AccessTokenExpiresAt: status.AccessTokenExpiresAt);
            providers.Add(new ModelServiceProvider(
                new RemoteModelProvider(id, runtime.DisplayName, runtime.Protocol, runtime.EndPoint, runtime.AuthMethod,
                    runtime.SupportsHostedImageGeneration, authentication, runtime.MaxOutputTokens, runtime.NetworkTimeoutSeconds),
                runtime, runtime.IsChatGptOAuth ? _authentication : null));
        }
        return Task.FromResult<IReadOnlyList<ModelServiceProvider>>(providers);
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
            if (!Uri.TryCreate(runtime.EndPoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
                throw new InvalidOperationException($"Provider '{id}' requires an HTTP endpoint.");
            if (!runtime.IsChatGptOAuth && string.IsNullOrWhiteSpace(runtime.ApiKey))
                throw new InvalidOperationException($"Provider '{id}' requires an API key.");
        }
        return config;
    }
}
