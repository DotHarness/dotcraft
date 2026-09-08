using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Configuration;
using DotCraft.Dreams;
using DotCraft.Modules;
using DotCraft.Sessions;
using DotCraft.Runtime;

namespace DotCraft.AppServer;

public interface IWorkspaceRuntimeAppServerFeatureFactory
{
    IWorkspaceRuntimeAppServerFeature Create(IServiceProvider services);
}

public interface IWorkspaceRuntimeAppServerFeature : IAsyncDisposable
{
    IChannelStatusProvider? ChannelStatusProvider { get; }

    IExternalChannelLogProvider? ExternalChannelLogProvider { get; }

    string? DashboardUrl { get; }

    event Action<DotCraft.Protocol.AppServer.AutomationUpdatedNotification>? AutomationUpdated;
    event Action<DotCraft.Protocol.AppServer.AutomationRunUpdatedNotification>? AutomationRunUpdated;

    Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);

    Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default);

    Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default);
}

public sealed class WorkspaceRuntimeAppServerFeatureContext(
    IServiceProvider services,
    AppConfig config,
    DotCraftPaths paths,
    ModuleRegistry moduleRegistry,
    ISessionService sessionService,
    DreamsService dreamsService,
    Action<BackgroundJobResult> emitBackgroundJobResult)
{
    public IServiceProvider Services { get; } = services;

    public AppConfig Config { get; } = config;

    public DotCraftPaths Paths { get; } = paths;

    public ModuleRegistry ModuleRegistry { get; } = moduleRegistry;

    public ISessionService SessionService { get; } = sessionService;



    public DreamsService DreamsService { get; } = dreamsService;


    public void EmitBackgroundJobResult(BackgroundJobResult result) =>
        emitBackgroundJobResult(result);
}
