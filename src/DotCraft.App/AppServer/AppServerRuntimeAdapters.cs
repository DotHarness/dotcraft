using DotCraft.Workspaces;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Dreams;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using DotCraft.Sessions;

namespace DotCraft.AppServer;

public interface IAppServerChannelRunner : IChannelStatusProvider, IExternalChannelLogProvider, IAsyncDisposable
{
    string? DashboardUrl { get; }

    void Initialize(
        ISessionService sessionService,
        CronService cronService,
        DreamsService dreamsService);

    Task StartWebPoolAsync();

    void BeginChannelLoops(CancellationToken ct);

    Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default);

    Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default);
}

public interface IAppServerChannelRunnerFactory
{
    IAppServerChannelRunner? Create(
        IServiceProvider services,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry moduleRegistry);
}

internal sealed class DefaultAppServerChannelRunnerFactory : IAppServerChannelRunnerFactory
{
    public IAppServerChannelRunner? Create(
        IServiceProvider services,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry moduleRegistry)
    {
        var runner = ChannelRunner.TryCreateForAppServer(services, config, paths, moduleRegistry);
        return runner == null ? null : new ChannelRunnerAdapter(runner);
    }
}

internal sealed class ChannelRunnerAdapter(ChannelRunner inner) : IAppServerChannelRunner
{
    public string? DashboardUrl => inner.DashBoardUrl;

    public void Initialize(
        ISessionService sessionService,
        CronService cronService,
        DreamsService dreamsService) =>
        inner.Initialize(sessionService, cronService, dreamsService);

    public Task StartWebPoolAsync() => inner.StartWebPoolAsync();

    public void BeginChannelLoops(CancellationToken ct) => inner.BeginChannelLoops(ct);

    public Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default) =>
        inner.ApplyExternalChannelUpsertAsync(entry, ct);

    public Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default) =>
        inner.ApplyExternalChannelRemoveAsync(channelName, ct);

    public IReadOnlyList<ChannelStatusSnapshot> GetChannelStatuses() => inner.GetChannelStatuses();

    public IReadOnlyList<string> GetRecentExternalChannelLogs(string channelName, int? tail = null) =>
        inner.GetRecentExternalChannelLogs(channelName, tail);

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

public interface IAppServerAutomationRuntime : IAsyncDisposable
{
    event Action<AutomationTask>? AutomationTaskUpdated;

    Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default);

    Task StopAsync(CancellationToken ct = default);
}

public interface IAppServerAutomationRuntimeFactory
{
    IAppServerAutomationRuntime? Create(IServiceProvider services);
}

internal sealed class DefaultAppServerAutomationRuntimeFactory : IAppServerAutomationRuntimeFactory
{
    public IAppServerAutomationRuntime? Create(IServiceProvider services)
    {
        return services.GetService<AutomationOrchestrator>() == null
            ? null
            : new AppServerAutomationRuntime(services);
    }
}

internal sealed class AppServerAutomationRuntime(IServiceProvider services) : IAppServerAutomationRuntime
{
    private readonly AutomationOrchestrator _orchestrator =
        services.GetRequiredService<AutomationOrchestrator>();

    private AutomationsEventDispatcher? _dispatcher;
    private bool _started;

    public event Action<AutomationTask>? AutomationTaskUpdated;

    public async Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
    {
        if (_started)
            throw new InvalidOperationException("AppServer automation runtime has already been started.");

        _dispatcher = new AutomationsEventDispatcher(
            _orchestrator,
            (task, _) => AutomationTaskUpdated?.Invoke(task));

        var automationSessionClient = new AutomationSessionClient(context.SessionService, context.Paths);
        _orchestrator.SetSessionClient(automationSessionClient);
        await _orchestrator.StartAsync(ct);
        _started = true;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _ = ct;
        if (!_started && _dispatcher == null)
            return;

        try
        {
            if (_started)
                await _orchestrator.StopAsync();
        }
        finally
        {
            _dispatcher?.Dispose();
            _dispatcher = null;
            _started = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
