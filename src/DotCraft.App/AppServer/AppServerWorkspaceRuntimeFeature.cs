using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DotCraft.DynamicWorkflows;

namespace DotCraft.AppServer;

public sealed class AppServerWorkspaceRuntimeFeatureFactory : IWorkspaceRuntimeAppServerFeatureFactory
{
    public IWorkspaceRuntimeAppServerFeature Create(IServiceProvider services) =>
        new AppServerWorkspaceRuntimeFeature(services);
}

internal sealed class AppServerWorkspaceRuntimeFeature(IServiceProvider services) : IWorkspaceRuntimeAppServerFeature
{
    private WorkspaceRuntimeAppServerFeatureContext? _context;
    private readonly IAppServerChannelRunnerFactory? _channelRunnerFactory =
        services.GetService<IAppServerChannelRunnerFactory>();
    private readonly IAppServerAutomationRuntimeFactory? _automationRuntimeFactory =
        services.GetService<IAppServerAutomationRuntimeFactory>();
    private readonly ILogger<AppServerWorkspaceRuntimeFeature> _logger =
        services.GetService<ILogger<AppServerWorkspaceRuntimeFeature>>()
        ?? NullLogger<AppServerWorkspaceRuntimeFeature>.Instance;
    private IAppServerChannelRunner? _channelRunner;
    private IAppServerAutomationRuntime? _automationRuntime;
    private IDynamicWorkflowService? _dynamicWorkflowService;
    private bool _started;

    public IChannelStatusProvider? ChannelStatusProvider => _channelRunner;

    public IExternalChannelLogProvider? ExternalChannelLogProvider => _channelRunner;

    public string? DashboardUrl => _channelRunner?.DashboardUrl;

    public event Action<DotCraft.Protocol.AppServer.AutomationUpdatedNotification>? AutomationUpdated;
    public event Action<DotCraft.Protocol.AppServer.AutomationRunUpdatedNotification>? AutomationRunUpdated;

    public async Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
    {
        if (_started || _context != null)
            throw new InvalidOperationException("AppServer workspace runtime feature has already been started.");

        _context = context;
        try
        {
            _channelRunner = _channelRunnerFactory?.Create(
                services,
                context.Config,
                context.Paths,
                context.ModuleRegistry);
            if (_channelRunner != null)
            {
                _channelRunner.Initialize(context.SessionService, context.DreamsService);
                await _channelRunner.StartWebPoolAsync();
            }

            _automationRuntime = _automationRuntimeFactory?.Create(services);
            if (_automationRuntime != null)
            {
                _automationRuntime.AutomationUpdated += OnAutomationUpdated;
                _automationRuntime.AutomationRunUpdated += OnAutomationRunUpdated;
                await _automationRuntime.StartAsync(context, ct);
            }

            _dynamicWorkflowService = services.GetService<IDynamicWorkflowService>();
            if (_dynamicWorkflowService != null)
                await _dynamicWorkflowService.StartAsync(ct);

            _channelRunner?.BeginChannelLoops(ct);
            _started = true;
        }
        catch
        {
            await StopAsync(ct);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (!_started
            && _context == null
            && _channelRunner == null
            && _automationRuntime == null)
            return;

        List<Exception>? errors = null;

        if (_automationRuntime != null)
        {
            try
            {
                _automationRuntime.AutomationUpdated -= OnAutomationUpdated;
                _automationRuntime.AutomationRunUpdated -= OnAutomationRunUpdated;
                await _automationRuntime.StopAsync(ct);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await _automationRuntime.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
            finally
            {
                _automationRuntime = null;
            }
        }

        if (_dynamicWorkflowService != null)
        {
            try
            {
                await _dynamicWorkflowService.StopAsync(ct);
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
            finally
            {
                _dynamicWorkflowService = null;
            }
        }

        if (_channelRunner != null)
        {
            try
            {
                await _channelRunner.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }
            finally
            {
                _channelRunner = null;
            }
        }

        _context = null;
        _started = false;

        if (errors is { Count: 1 })
            throw errors[0];
        if (errors is { Count: > 1 })
            throw new AggregateException(errors);
    }

    public async Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default)
    {
        if (_channelRunner == null)
            return;

        await _channelRunner.ApplyExternalChannelUpsertAsync(entry, ct);
    }

    public async Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default)
    {
        if (_channelRunner == null)
            return;

        await _channelRunner.ApplyExternalChannelRemoveAsync(channelName, ct);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private void OnAutomationUpdated(DotCraft.Protocol.AppServer.AutomationUpdatedNotification update) => AutomationUpdated?.Invoke(update);

    private void OnAutomationRunUpdated(DotCraft.Protocol.AppServer.AutomationRunUpdatedNotification update) => AutomationRunUpdated?.Invoke(update);
}
