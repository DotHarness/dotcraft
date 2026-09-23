using DotCraft.Agents;
using DotCraft.Agents.Remote;
using DotCraft.Auth.OpenAI;
using DotCraft.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.ModelService;

public sealed record ModelServiceCaller(string Id, object? State = null);

public sealed record ModelServiceProvider(
    RemoteModelProvider Description,
    EffectiveModelRuntime Runtime,
    OpenAIAuthManager? Authentication = null);

public interface IModelServiceAccess
{
    ValueTask<ModelServiceCaller?> AuthenticateAsync(HttpContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<ModelServiceProvider>> GetProvidersAsync(ModelServiceCaller caller, CancellationToken cancellationToken);
    ValueTask<IDisposable?> BeginRequestAsync(ModelServiceCaller caller, ModelServiceRequestContext request, CancellationToken cancellationToken);
}

public sealed record ModelServiceCall(
    ModelServiceCaller Caller,
    string ProviderId,
    ModelServiceRequestContext Context,
    int StatusCode,
    ProviderHttpUsage? Usage,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    bool Completed);

public interface IModelServiceObserver
{
    ValueTask OnCompletedAsync(ModelServiceCall call, CancellationToken cancellationToken);
}

public static class ModelServiceHosting
{
    public static IServiceCollection AddDotCraftModelService(this IServiceCollection services)
    {
        services.TryAddSingleton<ModelServiceProxy>();
        services.AddHttpClient("DotCraft.ModelService.Upstream", client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }

    public static IEndpointRouteBuilder MapDotCraftModelService(this IEndpointRouteBuilder endpoints, string prefix = "/model-service")
    {
        var group = endpoints.MapGroup(prefix);
        group.MapGet("/providers", (HttpContext context, ModelServiceProxy proxy) => proxy.CatalogAsync(context));
        group.MapMethods("/providers/{providerId}/http/{**operation}", ["GET", "POST"],
            (HttpContext context, ModelServiceProxy proxy, string providerId, string operation) =>
                proxy.ForwardAsync(context, providerId, operation));
        return endpoints;
    }
}
