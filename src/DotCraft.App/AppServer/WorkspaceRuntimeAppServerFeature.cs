using DotCraft.Workspaces;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Dreams;
using DotCraft.Heartbeat;
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

    event Action<AutomationTask>? AutomationTaskUpdated;

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
    AgentRunner agentRunner,
    CronService cronService,
    HeartbeatService heartbeatService,
    DreamsService dreamsService,
    Action<CronJob?, string, bool> emitCronStateChanged,
    Action<BackgroundJobResult> emitBackgroundJobResult)
{
    public IServiceProvider Services { get; } = services;

    public AppConfig Config { get; } = config;

    public DotCraftPaths Paths { get; } = paths;

    public ModuleRegistry ModuleRegistry { get; } = moduleRegistry;

    public ISessionService SessionService { get; } = sessionService;

    public AgentRunner AgentRunner { get; } = agentRunner;

    public CronService CronService { get; } = cronService;

    public HeartbeatService HeartbeatService { get; } = heartbeatService;

    public DreamsService DreamsService { get; } = dreamsService;

    public void EmitCronStateChanged(CronJob? job, string id, bool removed) =>
        emitCronStateChanged(job, id, removed);

    public void EmitBackgroundJobResult(BackgroundJobResult result) =>
        emitBackgroundJobResult(result);
}
