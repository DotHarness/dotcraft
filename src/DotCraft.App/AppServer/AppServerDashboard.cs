using System.Security.Cryptography;
using System.Text;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Dreams;
using DotCraft.Modules;
using DotCraft.Runtime;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.AppServer;

internal static class AppServerDashboard
{
    public static void ConfigureServices(IServiceCollection services, IServiceProvider workspaceServices)
    {
        services.AddSingleton(workspaceServices.GetRequiredService<IAppConfigMonitor>());
        services.AddSingleton(workspaceServices.GetRequiredService<ModelProviderRegistry>());
    }

    public static void MapManaged(WebApplication app, WorkspaceRuntime runtime, string publicPath, string token)
    {
        var traceStore = runtime.Services.GetRequiredService<TraceStore>();
        var credential = Encoding.UTF8.GetBytes(token);
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/dashboard", StringComparison.OrdinalIgnoreCase)
                && !HasCredential(context.Request, credential))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next(context);
        });

        MapWorkspace(app, runtime, traceStore, runtime.SessionService, runtime.DreamsService, publicPath);
    }

    public static void MapWorkspace(
        WebApplication app,
        WorkspaceRuntime runtime,
        TraceStore traceStore,
        ISessionService sessionService,
        DreamsService? dreamsService,
        string? publicPath = null)
    {
        app.MapDashBoardAuth(runtime.Config, publicPath ?? "/dashboard");
        app.UseDashBoardAuth(runtime.Config, publicPath ?? "/dashboard");
        app.MapDashBoard(traceStore, runtime.Paths, runtime.Services.GetService<UsageStatisticsStore>(),
            orchestratorProviders: runtime.Services.GetServices<IOrchestratorSnapshotProvider>(),
            configSchema: ConfigSchemaRegistrations.GetConfigSchema(),
            persistence: runtime.Services.GetRequiredService<SessionPersistenceService>(),
            deleteThreadAsync: (threadId, cancellationToken) => sessionService.DeleteThreadPermanentlyAsync(threadId, cancellationToken),
            sessionHandler: new DelegateDashBoardSessionHandler(id => sessionService.DeleteThreadPermanentlyAsync(id)),
            refreshTraceFromDiskBeforeRead: true,
            dreamsService: dreamsService,
            configuration: runtime.Configuration,
            authEnabled: DashBoardAuth.IsEnabled(runtime.Config),
            includeFrontend: publicPath is null);
    }

    private static bool HasCredential(HttpRequest request, byte[] credential) =>
        request.Headers.Authorization is { Count: 1 } values
        && values[0] is { } header
        && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[7..]), credential);
}
