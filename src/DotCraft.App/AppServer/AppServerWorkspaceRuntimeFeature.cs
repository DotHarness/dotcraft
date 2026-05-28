using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Channels;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Gateway;
using DotCraft.Hosting;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

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
    private IAppServerChannelRunner? _channelRunner;
    private IAppServerAutomationRuntime? _automationRuntime;
    private bool _started;

    public IChannelStatusProvider? ChannelStatusProvider => _channelRunner;

    public IExternalChannelLogProvider? ExternalChannelLogProvider => _channelRunner;

    public string? DashboardUrl => _channelRunner?.DashboardUrl;

    public event Action<IAutomationTaskEventPayload>? AutomationTaskUpdated;

    public async Task StartAsync(WorkspaceRuntimeAppServerFeatureContext context, CancellationToken ct = default)
    {
        if (_started || _context != null)
            throw new InvalidOperationException("AppServer workspace runtime feature has already been started.");

        _context = context;
        try
        {
            var messageRouter = services.GetRequiredService<MessageRouter>();

            context.CronService.CronJobPersistedAfterExecution = (job, id, removed) =>
                context.EmitCronStateChanged(job, id, removed);

            context.CronService.OnJob = async job =>
            {
                var sessionKey = $"cron:{job.Id}";
                AgentRunResult? run;
                try
                {
                    run = await context.AgentRunner.RunAsync(job.Payload.Message, sessionKey, job.Name, ct);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[grey][[AppServer]][/] [red]Cron job {job.Id} failed: {Markup.Escape(ex.Message)}[/]");
                    return new CronOnJobResult(null, null, ex.Message, false, null, null);
                }

                var channel = job.Payload.Channel;
                var isCliChannel = channel == null
                    || string.Equals(channel, "cli", StringComparison.OrdinalIgnoreCase);
                if (job.Payload.Deliver && isCliChannel)
                {
                    context.EmitBackgroundJobResult(new BackgroundJobResult(
                        "cron",
                        job.Id,
                        job.Name,
                        run?.Error == null ? run?.Result : null,
                        run?.Error,
                        run?.ThreadId,
                        run?.InputTokens,
                        run?.OutputTokens));
                }
                else if (job.Payload.Deliver
                         && !isCliChannel
                         && !string.IsNullOrEmpty(channel))
                {
                    var target = job.Payload.To ?? job.Payload.CreatorId ?? "";
                    var content = run?.Error == null
                        ? (run?.Result ?? "")
                        : $"[Cron] {job.Name}\n{run?.Error}";
                    if (!string.IsNullOrEmpty(content) || run?.Error != null)
                    {
                        await messageRouter.DeliverAsync(
                            channel,
                            target,
                            new ChannelOutboundMessage
                            {
                                Kind = "text",
                                Text = content
                            });
                    }
                }

                var ok = run != null && run.Error == null;
                return new CronOnJobResult(run?.ThreadId, run?.Result, run?.Error, ok, run?.InputTokens, run?.OutputTokens);
            };

            if (context.Config.Heartbeat.NotifyAdmin)
            {
                context.HeartbeatService.OnResult = async result =>
                    await messageRouter.BroadcastToAdminsAsync($"[Heartbeat] {result}");
            }

            _automationRuntime = _automationRuntimeFactory?.Create(services);
            if (_automationRuntime != null)
            {
                _automationRuntime.AutomationTaskUpdated += OnAutomationTaskUpdated;
                await _automationRuntime.StartAsync(context, ct);
            }

            _channelRunner = _channelRunnerFactory?.Create(
                services,
                context.Config,
                context.Paths,
                context.ModuleRegistry);
            if (_channelRunner != null)
            {
                _channelRunner.Initialize(context.SessionService, context.HeartbeatService, context.CronService, context.DreamsService);
                await _channelRunner.StartWebPoolAsync();
            }

            if (context.Config.Cron.Enabled)
            {
                context.CronService.Start();
                AnsiConsole.MarkupLine(
                    $"[grey][[AppServer]][/] Cron service started ({context.CronService.ListJobs().Count} jobs)");
            }

            if (context.Config.Heartbeat.Enabled)
            {
                context.HeartbeatService.Start();
                AnsiConsole.MarkupLine(
                    $"[grey][[AppServer]][/] Heartbeat started (interval: {context.Config.Heartbeat.IntervalSeconds}s)");
            }

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

        if (_automationRuntime != null)
        {
            try
            {
                _automationRuntime.AutomationTaskUpdated -= OnAutomationTaskUpdated;
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

        if (_context != null)
        {
            _context.CronService.CronJobPersistedAfterExecution = null;
            _context.CronService.OnJob = null;
            _context.HeartbeatService.OnResult = null;
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

    private void OnAutomationTaskUpdated(IAutomationTaskEventPayload task)
    {
        AutomationTaskUpdated?.Invoke(task);
    }
}
