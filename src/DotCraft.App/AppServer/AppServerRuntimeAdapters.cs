using DotCraft.Workspaces;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Channels;
using DotCraft.Configuration;
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
        DreamsService dreamsService) =>
        inner.Initialize(sessionService, dreamsService);

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
    event Action<DotCraft.Protocol.AppServer.AutomationUpdatedNotification>? AutomationUpdated;
    event Action<DotCraft.Protocol.AppServer.AutomationRunUpdatedNotification>? AutomationRunUpdated;
    Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

public interface IAppServerAutomationRuntimeFactory
{
    IAppServerAutomationRuntime? Create(IServiceProvider services);
}

internal sealed class DefaultAppServerAutomationRuntimeFactory : IAppServerAutomationRuntimeFactory
{
    public IAppServerAutomationRuntime? Create(IServiceProvider services) =>
        services.GetService<AutomationService>() == null ? null : new AppServerAutomationRuntime(services);
}

internal sealed class AppServerAutomationRuntime(IServiceProvider services) : IAppServerAutomationRuntime
{
    private readonly AutomationService _service = services.GetRequiredService<AutomationService>();
    private bool _started;
    public event Action<DotCraft.Protocol.AppServer.AutomationUpdatedNotification>? AutomationUpdated;
    public event Action<DotCraft.Protocol.AppServer.AutomationRunUpdatedNotification>? AutomationRunUpdated;

    public async Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
    {
        if (_started) throw new InvalidOperationException("Automation runtime has already started.");
        _started = true;
        _service.Updated += OnUpdated;
        _service.RunUpdated += OnRunUpdated;
        _service.SetSessionClient(new AutomationSessionClient(context.SessionService, context.Paths));
        _service.AppConfigMonitor = services.GetService<DotCraft.Configuration.IAppConfigMonitor>();
        _service.DeliverAsync = async (definition, run, token) =>
        {
            var origin = definition.Origin;
            if (origin == null || origin.Channel is "cli" or "api" or "acp")
            {
                context.EmitBackgroundJobResult(new DotCraft.Runtime.BackgroundJobResult(
                    "automation", definition.Id, definition.Name, run.Summary, run.Error,
                    run.ThreadId, null, null));
                return;
            }
            var target = origin.DeliveryTarget ?? origin.GroupId ?? origin.UserId;
            await services.GetRequiredService<DotCraft.Channels.MessageRouter>().DeliverRequiredAsync(
                origin.Channel, target, new DotCraft.Channels.ChannelDeliveryMessage
                { Kind = "text", Text = run.Error ?? run.Summary ?? "Automation completed." }, token);
        };
        try { await _service.StartAsync(ct); }
        catch { await StopAsync(CancellationToken.None); throw; }
    }

    private Task OnUpdated(AutomationDefinition? definition, string id, bool removed)
    {
        AutomationUpdated?.Invoke(new() { AutomationId = id, Removed = removed,
            Automation = definition == null ? null : AutomationsRequestHandler.ToWire(definition) });
        return Task.CompletedTask;
    }

    private Task OnRunUpdated(AutomationRun run)
    {
        AutomationRunUpdated?.Invoke(new() { Run = AutomationsRequestHandler.ToWire(run) });
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_started) return;
        try { await _service.StopAsync(ct); }
        finally
        {
            _service.Updated -= OnUpdated;
            _service.RunUpdated -= OnRunUpdated;
            _service.DeliverAsync = null;
            _started = false;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
